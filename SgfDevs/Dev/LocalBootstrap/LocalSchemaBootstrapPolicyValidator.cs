#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SgfDevs.Dev.LocalBootstrap;

/// <summary>
/// Pure structural result for future schema v2 profiles. This is not a runtime token:
/// no signature, DB state, receipt, package plan, handler factory or service has been verified.
/// Future runtime code must derive the firstboot profile, reuse profile and DB basename from
/// the same safe id before builder, then bind one logical DB id and both distinct phase
/// signatures into the DB receipt. This validator only checks the in-memory effective policy.
/// </summary>
public sealed record LocalSchemaBootstrapEffectivePolicy(
    LocalSchemaBootstrapPhase Phase,
    string LogicalDbId,
    string SchemaSourceManifestHash,
    Uri? FirstbootApplicationUrl,
    IReadOnlyList<string> EnabledUSyncHandlers,
    IReadOnlyList<string> DisabledUSyncHandlers);

public static class LocalSchemaBootstrapPolicyValidator
{
    public const string RequiredCreatedBy = "scripts/bootstrap-local-cms.py";
    public const string RequiredDefaultSet = "Default";
    public const string RequiredGroup = "Settings";
    public const string RequiredAction = "Import";
    public const string RequiredStartupPolicy = "None";
    public const string RequiredFirstbootModelsMode = "Nothing";
    public const string RequiredReuseModelsMode = "SourceCodeManual";
    public const string RequiredFirstbootRuntimeMode = "Production";
    public const string ReuseRuntimeMode = "Development";

    public static readonly IReadOnlyList<string> AllowedHandlers =
    [
        "LanguageHandler",
        "DataTypeHandler",
        "TemplateHandler",
        "ContentTypeHandler",
        "MediaTypeHandler",
        "MemberTypeHandler",
        "RelationTypeHandler"
    ];

    public static readonly IReadOnlyList<string> DeniedHandlers =
    [
        "ContentHandler",
        "MediaHandler",
        "ContentTemplateHandler",
        "ElementHandler",
        "DictionaryHandler",
        "DomainHandler",
        "WebhookHandler"
    ];

    public static LocalSchemaBootstrapEffectivePolicy Validate(IConfiguration configuration, string environmentName)
    {
        RequireDevelopment(environmentName);
        var phase = ValidatePhasePolicy(configuration);
        ValidateUnattendedPolicy(configuration, phase);
        ValidateUSyncPolicy(configuration, phase);
        var firstbootUrl = ValidateRuntimePolicy(configuration, phase);

        return new LocalSchemaBootstrapEffectivePolicy(
            phase,
            RequireValue(configuration, "SGFDevs:LocalBootstrap:LogicalDbId"),
            RequireValue(configuration, "SGFDevs:LocalBootstrap:SchemaSourceManifestHash"),
            firstbootUrl,
            AllowedHandlers,
            DeniedHandlers);
    }

    private static LocalSchemaBootstrapPhase ValidatePhasePolicy(IConfiguration configuration)
    {
        if (!RequireBoolean(configuration, "SGFDevs:LocalBootstrap:Enabled"))
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap v2 requires SGFDevs:LocalBootstrap:Enabled to be true.");
        }

        RequireValue(configuration, "SGFDevs:LocalBootstrap:CreatedBy", RequiredCreatedBy);
        if (RequireInteger(configuration, "SGFDevs:LocalBootstrap:PolicyVersion") != LocalSchemaBootstrapGuard.PolicyVersion)
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap v2 requires PolicyVersion 2.");
        }

        var phaseValue = RequireValue(configuration, "SGFDevs:LocalBootstrap:Phase");
        var phase = phaseValue switch
        {
            LocalSchemaBootstrapGuard.PhaseFirstboot => LocalSchemaBootstrapPhase.Firstboot,
            LocalSchemaBootstrapGuard.PhaseReuse => LocalSchemaBootstrapPhase.Reuse,
            _ => throw new LocalBootstrapConfigurationException("Schema bootstrap v2 requires an exact schema phase.")
        };

        RequireMarker(configuration, "SGFDevs:LocalBootstrap:LogicalDbId");
        RequireSourceManifestMarker(configuration, "SGFDevs:LocalBootstrap:SchemaSourceManifestHash");

        if (!RequireBoolean(configuration, "SGFDevs:LocalBootstrap:SchemaSourceOptIn"))
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap v2 requires SchemaSourceOptIn true.");
        }

        if (RequireBoolean(configuration, "SGFDevs:LocalBootstrap:SeedFictionalContent"))
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap v2 does not allow fictional seed content.");
        }

        var importOnFirstBoot = RequireBoolean(configuration, "SGFDevs:LocalBootstrap:SchemaImportOnFirstBoot");
        if (phase is LocalSchemaBootstrapPhase.Firstboot && !importOnFirstBoot)
        {
            throw new LocalBootstrapConfigurationException("Schema firstboot requires SchemaImportOnFirstBoot true.");
        }

        if (phase is LocalSchemaBootstrapPhase.Reuse && importOnFirstBoot)
        {
            throw new LocalBootstrapConfigurationException("Schema reuse requires SchemaImportOnFirstBoot false.");
        }

        return phase;
    }

    private static void ValidateUnattendedPolicy(IConfiguration configuration, LocalSchemaBootstrapPhase phase)
    {
        var installUnattended = RequireBoolean(configuration, "Umbraco:CMS:Unattended:InstallUnattended");
        var upgradeUnattended = RequireBoolean(configuration, "Umbraco:CMS:Unattended:UpgradeUnattended");
        var packageMigrationsUnattended = RequireBoolean(configuration, "Umbraco:CMS:Unattended:PackageMigrationsUnattended");

        if (phase is LocalSchemaBootstrapPhase.Firstboot)
        {
            if (!installUnattended || upgradeUnattended || packageMigrationsUnattended)
            {
                throw new LocalBootstrapConfigurationException("Schema firstboot requires install unattended only.");
            }

            return;
        }

        if (installUnattended || upgradeUnattended || packageMigrationsUnattended)
        {
            throw new LocalBootstrapConfigurationException("Schema reuse requires all unattended switches to be false.");
        }
    }

    private static void ValidateUSyncPolicy(IConfiguration configuration, LocalSchemaBootstrapPhase phase)
    {
        RequireValue(configuration, "uSync:Settings:DefaultSet", RequiredDefaultSet);
        RequireValue(configuration, "uSync:Settings:ImportAtStartup", RequiredStartupPolicy);
        RequireValue(configuration, "uSync:Settings:ExportAtStartup", RequiredStartupPolicy);
        RequireValue(configuration, "uSync:Settings:ExportOnSave", RequiredStartupPolicy);

        var importOnFirstBoot = RequireBoolean(configuration, "uSync:Settings:ImportOnFirstBoot");
        var firstBootGroup = RequireValue(configuration, "uSync:Settings:FirstBootGroup", allowEmpty: phase is LocalSchemaBootstrapPhase.Reuse);
        if (phase is LocalSchemaBootstrapPhase.Firstboot)
        {
            if (!importOnFirstBoot || !string.Equals(firstBootGroup, RequiredGroup, StringComparison.Ordinal))
            {
                throw new LocalBootstrapConfigurationException("Schema firstboot requires uSync FirstBoot import for the Settings group.");
            }
        }
        else if (importOnFirstBoot || firstBootGroup.Length != 0)
        {
            throw new LocalBootstrapConfigurationException("Schema reuse must leave uSync FirstBoot disabled.");
        }

        RequireBoolean(configuration, "uSync:Sets:Default:Enabled", expected: true);
        RequireBoolean(configuration, "uSync:Sets:Default:HandlerDefaults:Enabled", expected: false);
        RequireExactArray(configuration, "uSync:Sets:Default:HandlerDefaults:Actions", [RequiredAction]);
        RequireExactArray(configuration, "uSync:Sets:Default:DisabledHandlers", DeniedHandlers);

        foreach (var alias in AllowedHandlers)
        {
            var handler = configuration.GetSection($"uSync:Sets:Default:Handlers:{alias}");
            if (!handler.Exists())
            {
                throw new LocalBootstrapConfigurationException($"Schema uSync policy requires {alias}.");
            }

            RequireBoolean(configuration, $"uSync:Sets:Default:Handlers:{alias}:Enabled", expected: true);
            var group = handler["Group"];
            if (alias == "RelationTypeHandler")
            {
                RequireValue(configuration, $"uSync:Sets:Default:Handlers:{alias}:Group", RequiredGroup);
            }
            else if (!string.IsNullOrWhiteSpace(group) && !string.Equals(group.Trim(), RequiredGroup, StringComparison.Ordinal))
            {
                throw new LocalBootstrapConfigurationException($"Schema uSync policy requires {alias} to stay in the Settings group.");
            }

            var actionSection = handler.GetSection("Actions");
            if (actionSection.Exists())
            {
                RequireExactArray(configuration, $"uSync:Sets:Default:Handlers:{alias}:Actions", [RequiredAction]);
            }
        }

        foreach (var alias in DeniedHandlers)
        {
            var handler = configuration.GetSection($"uSync:Sets:Default:Handlers:{alias}");
            if (!handler.Exists())
            {
                throw new LocalBootstrapConfigurationException($"Schema uSync policy requires {alias} to be explicitly disabled.");
            }

            RequireBoolean(configuration, $"uSync:Sets:Default:Handlers:{alias}:Enabled", expected: false);
        }

        var knownAliases = AllowedHandlers.Concat(DeniedHandlers).ToHashSet(StringComparer.Ordinal);
        foreach (var handler in configuration.GetSection("uSync:Sets:Default:Handlers").GetChildren())
        {
            if (!knownAliases.Contains(handler.Key) && OptionalBoolean(handler.GetSection("Enabled")) is true)
            {
                throw new LocalBootstrapConfigurationException($"Schema uSync policy does not allow unknown enabled handler {handler.Key}.");
            }
        }
    }

    private static Uri? ValidateRuntimePolicy(IConfiguration configuration, LocalSchemaBootstrapPhase phase)
    {
        if (phase is LocalSchemaBootstrapPhase.Firstboot)
        {
            RequireValue(configuration, "Umbraco:CMS:Runtime:Mode", RequiredFirstbootRuntimeMode);
            RequireValue(configuration, "Umbraco:CMS:ModelsBuilder:ModelsMode", RequiredFirstbootModelsMode);
            RequireBoolean(configuration, "Umbraco:CMS:Global:UseHttps", expected: true);
            return RequireControlledLoopbackUrl(configuration, "Umbraco:CMS:WebRouting:UmbracoApplicationUrl");
        }

        var runtimeMode = configuration["Umbraco:CMS:Runtime:Mode"];
        if (!string.IsNullOrWhiteSpace(runtimeMode) &&
            !string.Equals(runtimeMode.Trim(), ReuseRuntimeMode, StringComparison.Ordinal))
        {
            throw new LocalBootstrapConfigurationException("Schema reuse must not carry the private firstboot Production runtime mode.");
        }

        RequireValue(configuration, "Umbraco:CMS:ModelsBuilder:ModelsMode", RequiredReuseModelsMode);
        RequireBoolean(configuration, "Umbraco:CMS:Global:UseHttps", expected: false);
        if (!string.IsNullOrWhiteSpace(configuration["Umbraco:CMS:WebRouting:UmbracoApplicationUrl"]))
        {
            throw new LocalBootstrapConfigurationException("Schema reuse must not carry a private firstboot application URL.");
        }

        return null;
    }

    private static void RequireDevelopment(string environmentName)
    {
        if (!string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap v2 is only valid in Development.");
        }
    }

    private static string RequireValue(IConfiguration configuration, string key, string? expected = null, bool allowEmpty = false)
    {
        var value = configuration[key];
        if (value is null || (!allowEmpty && value.Length == 0) || value.Any(char.IsControl) || value != value.Trim())
        {
            throw new LocalBootstrapConfigurationException($"Schema bootstrap requires {key}.");
        }

        if (expected is not null && !string.Equals(value, expected, StringComparison.Ordinal))
        {
            throw new LocalBootstrapConfigurationException($"Schema bootstrap requires {key} to be {expected}.");
        }

        return value;
    }

    private static bool RequireBoolean(IConfiguration configuration, string key, bool? expected = null)
    {
        var value = RequireValue(configuration, key);
        if (!bool.TryParse(value, out var parsed))
        {
            throw new LocalBootstrapConfigurationException($"Schema bootstrap requires {key} to be true or false.");
        }

        if (expected is not null && parsed != expected.Value)
        {
            throw new LocalBootstrapConfigurationException($"Schema bootstrap requires {key} to be {expected.Value.ToString().ToLowerInvariant()}.");
        }

        return parsed;
    }

    private static bool? OptionalBoolean(IConfigurationSection section)
    {
        if (!section.Exists())
        {
            return null;
        }

        return bool.TryParse(section.Value, out var parsed) ? parsed : null;
    }

    private static int RequireInteger(IConfiguration configuration, string key)
    {
        var value = RequireValue(configuration, key);
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new LocalBootstrapConfigurationException($"Schema bootstrap requires {key} to be an integer.");
        }

        return parsed;
    }

    private static void RequireMarker(IConfiguration configuration, string key)
    {
        var value = RequireValue(configuration, key);
        if (value.Length < LocalSchemaBootstrapGuard.MinimumSafeIdLength ||
            value.Any(static c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')) ||
            value.StartsWith("-", StringComparison.Ordinal) || value.EndsWith("-", StringComparison.Ordinal) ||
            value.Contains("--", StringComparison.Ordinal))
        {
            throw new LocalBootstrapConfigurationException($"Schema bootstrap requires a valid {key} marker.");
        }
    }

    private static void RequireSourceManifestMarker(IConfiguration configuration, string key)
    {
        var value = RequireValue(configuration, key);
        const string prefix = "sha256:";
        var hash = value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;
        if (hash.Length != 64 || hash.Any(static c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')))
        {
            throw new LocalBootstrapConfigurationException($"Schema bootstrap requires a valid {key} marker.");
        }
    }

    private static void RequireExactArray(IConfiguration configuration, string key, IReadOnlyList<string> expected)
    {
        var section = configuration.GetSection(key);
        if (!section.Exists() || section.Value is not null)
        {
            throw new LocalBootstrapConfigurationException($"Schema bootstrap requires {key} as a configuration array.");
        }

        var values = section.GetChildren()
            .OrderBy(static child => ParseArrayIndex(child.Key))
            .Select(static child => child.Value)
            .ToArray();

        if (values.Length != expected.Count)
        {
            throw new LocalBootstrapConfigurationException($"Schema bootstrap requires {key} to contain the approved values only.");
        }

        for (var index = 0; index < expected.Count; index++)
        {
            if (!string.Equals(values[index], expected[index], StringComparison.Ordinal))
            {
                throw new LocalBootstrapConfigurationException($"Schema bootstrap requires {key} to contain the approved values only.");
            }
        }
    }

    private static int ParseArrayIndex(string key) =>
        int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : int.MaxValue;

    private static Uri RequireControlledLoopbackUrl(IConfiguration configuration, string key)
    {
        var value = RequireValue(configuration, key);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new LocalBootstrapConfigurationException("Schema firstboot requires a controlled HTTPS loopback application URL.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.AbsolutePath.Length != 0 && uri.AbsolutePath != "/") || uri.Port < 1024)
        {
            throw new LocalBootstrapConfigurationException("Schema firstboot requires an origin-only loopback application URL with an explicit run port.");
        }

        if (!IsLoopbackHost(uri.Host))
        {
            throw new LocalBootstrapConfigurationException("Schema firstboot requires a loopback application URL.");
        }

        return uri;
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
