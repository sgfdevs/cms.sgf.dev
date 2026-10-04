using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SgfDevs.Dev.LocalBootstrap;
using Xunit;

namespace SgfDevs.Tests;

public sealed class LocalSchemaBootstrapPolicyValidatorTests
{
    private const string ManifestHash = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData(LocalSchemaBootstrapGuard.PhaseFirstboot, LocalSchemaBootstrapPhase.Firstboot)]
    [InlineData(LocalSchemaBootstrapGuard.PhaseReuse, LocalSchemaBootstrapPhase.Reuse)]
    public void Validate_AcceptsFuturePhasePoliciesStructurally(string phaseValue, LocalSchemaBootstrapPhase phase)
    {
        var configuration = BuildConfiguration(phaseValue);

        var policy = LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development);

        Assert.Equal(phase, policy.Phase);
        Assert.Equal("schema-v2-abcdefghijkl", policy.LogicalDbId);
        Assert.Equal(ManifestHash, policy.SchemaSourceManifestHash);
        Assert.Equal(LocalSchemaBootstrapPolicyValidator.AllowedHandlers, policy.EnabledUSyncHandlers);
        Assert.Equal(LocalSchemaBootstrapPolicyValidator.DeniedHandlers, policy.DisabledUSyncHandlers);
        Assert.Equal(phase is LocalSchemaBootstrapPhase.Firstboot ? "https://127.0.0.1:55443/" : null, policy.FirstbootApplicationUrl?.ToString());
    }

    [Theory]
    [InlineData("SGFDevs:LocalBootstrap:Enabled", null)]
    [InlineData("SGFDevs:LocalBootstrap:Enabled", "false")]
    [InlineData("SGFDevs:LocalBootstrap:CreatedBy", "manual")]
    [InlineData("SGFDevs:LocalBootstrap:PolicyVersion", null)]
    [InlineData("SGFDevs:LocalBootstrap:PolicyVersion", "not-an-int")]
    [InlineData("SGFDevs:LocalBootstrap:PolicyVersion", "3")]
    [InlineData("SGFDevs:LocalBootstrap:Phase", "SCHEMA-FIRSTBOOT")]
    [InlineData("SGFDevs:LocalBootstrap:LogicalDbId", "")]
    [InlineData("SGFDevs:LocalBootstrap:LogicalDbId", "../escape")]
    [InlineData("SGFDevs:LocalBootstrap:SchemaSourceOptIn", "false")]
    [InlineData("SGFDevs:LocalBootstrap:SchemaSourceManifestHash", "not-a-sha")]
    [InlineData("SGFDevs:LocalBootstrap:SeedFictionalContent", "true")]
    [InlineData("SGFDevs:LocalBootstrap:SchemaImportOnFirstBoot", "false")]
    public void Validate_RejectsMissingFalseMalformedOrInconsistentPhaseFields(string key, string? value)
    {
        var overrides = new Dictionary<string, string?> { [key] = value };
        var configuration = BuildConfiguration(LocalSchemaBootstrapGuard.PhaseFirstboot, overrides);

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development));
    }

    [Fact]
    public void Validate_ParsesPolicyVersionSemantically()
    {
        var configuration = BuildConfiguration(
            LocalSchemaBootstrapGuard.PhaseFirstboot,
            new Dictionary<string, string?>
            {
                ["SGFDevs:LocalBootstrap:PolicyVersion"] = "02"
            });

        LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development);
    }

    [Fact]
    public void Validate_RejectsReuseImportFlagMismatch()
    {
        var configuration = BuildConfiguration(
            LocalSchemaBootstrapGuard.PhaseReuse,
            new Dictionary<string, string?>
            {
                ["SGFDevs:LocalBootstrap:SchemaImportOnFirstBoot"] = "true"
            });

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development));
    }

    [Theory]
    [InlineData("Umbraco:CMS:Unattended:InstallUnattended", "false")]
    [InlineData("Umbraco:CMS:Unattended:UpgradeUnattended", "true")]
    [InlineData("Umbraco:CMS:Unattended:PackageMigrationsUnattended", "true")]
    public void Validate_RejectsFirstbootUnattendedTamper(string key, string value)
    {
        var configuration = BuildConfiguration(LocalSchemaBootstrapGuard.PhaseFirstboot, new Dictionary<string, string?> { [key] = value });

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development));
    }

    [Theory]
    [InlineData("Umbraco:CMS:Unattended:InstallUnattended")]
    [InlineData("Umbraco:CMS:Unattended:UpgradeUnattended")]
    [InlineData("Umbraco:CMS:Unattended:PackageMigrationsUnattended")]
    public void Validate_RejectsReuseUnattendedTamper(string key)
    {
        var configuration = BuildConfiguration(LocalSchemaBootstrapGuard.PhaseReuse, new Dictionary<string, string?> { [key] = "true" });

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development));
    }

    [Fact]
    public void Validate_RequiresSevenAllowedAndSevenDeniedHandlers()
    {
        var configuration = BuildConfiguration(LocalSchemaBootstrapGuard.PhaseFirstboot);

        var policy = LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development);

        Assert.Equal(
            ["LanguageHandler", "DataTypeHandler", "TemplateHandler", "ContentTypeHandler", "MediaTypeHandler", "MemberTypeHandler", "RelationTypeHandler"],
            policy.EnabledUSyncHandlers);
        Assert.Equal(
            ["ContentHandler", "MediaHandler", "ContentTemplateHandler", "ElementHandler", "DictionaryHandler", "DomainHandler", "WebhookHandler"],
            policy.DisabledUSyncHandlers);
    }

    [Theory]
    [InlineData("uSync:Sets:Default:Handlers:LanguageHandler:Enabled", "false")]
    [InlineData("uSync:Sets:Default:Handlers:ContentHandler:Enabled", "true")]
    [InlineData("uSync:Sets:Default:Handlers:RelationTypeHandler:Group", "Content")]
    [InlineData("uSync:Sets:Default:Handlers:ContentTypeHandler:Group", "Content")]
    public void Validate_RejectsHandlerEnabledAndGroupTamper(string key, string value)
    {
        var configuration = BuildConfiguration(LocalSchemaBootstrapGuard.PhaseFirstboot, new Dictionary<string, string?> { [key] = value });

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development));
    }

    [Fact]
    public void Validate_RejectsUnknownFutureEnabledHandler()
    {
        var configuration = BuildConfiguration(
            LocalSchemaBootstrapGuard.PhaseFirstboot,
            new Dictionary<string, string?>
            {
                ["uSync:Sets:Default:Handlers:FutureHandler:Enabled"] = "true"
            });

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development));
    }

    [Theory]
    [InlineData("uSync:Settings:ImportAtStartup", "Import")]
    [InlineData("uSync:Settings:ExportAtStartup", "All")]
    [InlineData("uSync:Settings:ExportOnSave", "All")]
    [InlineData("uSync:Settings:DefaultSet", "Schema")]
    [InlineData("uSync:Sets:Default:HandlerDefaults:Enabled", "true")]
    public void Validate_RejectsStartupDefaultAndInheritedSettingTamper(string key, string value)
    {
        var configuration = BuildConfiguration(LocalSchemaBootstrapGuard.PhaseFirstboot, new Dictionary<string, string?> { [key] = value });

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development));
    }

    [Fact]
    public void Validate_RejectsConfigurationArrayMergeThatAddsDefaultExportAction()
    {
        var configuration = BuildConfiguration(
            LocalSchemaBootstrapGuard.PhaseFirstboot,
            new Dictionary<string, string?>
            {
                ["uSync:Sets:Default:HandlerDefaults:Actions:1"] = "Export"
            });

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development));
    }

    [Fact]
    public void Validate_AcceptsAllowedHandlerChildActionWhenItStillMatchesImportOnly()
    {
        var configuration = BuildConfiguration(
            LocalSchemaBootstrapGuard.PhaseFirstboot,
            new Dictionary<string, string?>
            {
                ["uSync:Sets:Default:Handlers:TemplateHandler:Actions:0"] = "Import"
            });

        LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development);
    }

    [Fact]
    public void Validate_RejectsAllowedHandlerChildExportAction()
    {
        var configuration = BuildConfiguration(
            LocalSchemaBootstrapGuard.PhaseFirstboot,
            new Dictionary<string, string?>
            {
                ["uSync:Sets:Default:Handlers:TemplateHandler:Actions:0"] = "Import",
                ["uSync:Sets:Default:Handlers:TemplateHandler:Actions:1"] = "Export"
            });

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development));
    }

    [Theory]
    [InlineData("uSync:Settings:ImportOnFirstBoot", "false")]
    [InlineData("uSync:Settings:FirstBootGroup", "")]
    public void Validate_RejectsFirstbootStartupFlagTamper(string key, string value)
    {
        var configuration = BuildConfiguration(LocalSchemaBootstrapGuard.PhaseFirstboot, new Dictionary<string, string?> { [key] = value });

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development));
    }

    [Theory]
    [InlineData("uSync:Settings:ImportOnFirstBoot", "true")]
    [InlineData("uSync:Settings:FirstBootGroup", "Settings")]
    public void Validate_RejectsReuseStartupFlagTamper(string key, string value)
    {
        var configuration = BuildConfiguration(LocalSchemaBootstrapGuard.PhaseReuse, new Dictionary<string, string?> { [key] = value });

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development));
    }

    [Theory]
    [InlineData("http://127.0.0.1:55443/")]
    [InlineData("https://example.com:55443/")]
    [InlineData("https://127.0.0.1:55443/umbraco")]
    [InlineData("https://user@127.0.0.1:55443/")]
    [InlineData("https://127.0.0.1:55443/?x=1")]
    [InlineData("https://127.0.0.1/")]
    public void Validate_RejectsUnsafeFirstbootRuntimeUrl(string url)
    {
        var configuration = BuildConfiguration(
            LocalSchemaBootstrapGuard.PhaseFirstboot,
            new Dictionary<string, string?>
            {
                ["Umbraco:CMS:WebRouting:UmbracoApplicationUrl"] = url
            });

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development));
    }

    [Theory]
    [InlineData("Umbraco:CMS:Runtime:Mode", "Development")]
    [InlineData("Umbraco:CMS:ModelsBuilder:ModelsMode", "SourceCodeManual")]
    [InlineData("Umbraco:CMS:Global:UseHttps", "false")]
    public void Validate_RejectsFirstbootRuntimeTamper(string key, string value)
    {
        var configuration = BuildConfiguration(LocalSchemaBootstrapGuard.PhaseFirstboot, new Dictionary<string, string?> { [key] = value });

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development));
    }

    [Theory]
    [InlineData("Umbraco:CMS:Runtime:Mode", "Production")]
    [InlineData("Umbraco:CMS:ModelsBuilder:ModelsMode", "Nothing")]
    [InlineData("Umbraco:CMS:Global:UseHttps", "true")]
    [InlineData("Umbraco:CMS:WebRouting:UmbracoApplicationUrl", "https://127.0.0.1:55443/")]
    public void Validate_RejectsPrivateFirstbootRuntimeSettingsCarriedIntoReuse(string key, string value)
    {
        var configuration = BuildConfiguration(LocalSchemaBootstrapGuard.PhaseReuse, new Dictionary<string, string?> { [key] = value });

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development));
    }

    [Fact]
    public void Validate_UsesCaseInsensitiveConfigurationProviderKeys()
    {
        var values = BaseValues(LocalSchemaBootstrapGuard.PhaseFirstboot);
        values.Remove("SGFDevs:LocalBootstrap:SchemaSourceOptIn");
        values["sgfdevs:localbootstrap:scheMAsourceOPTin"] = "true";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Development);
    }

    [Fact]
    public void Validate_RejectsNonDevelopmentEnvironment()
    {
        var configuration = BuildConfiguration(LocalSchemaBootstrapGuard.PhaseFirstboot);

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapPolicyValidator.Validate(configuration, Environments.Production));
    }

    [Fact]
    public void SupportCapAndProgramRejectionRemainUnchanged()
    {
        Assert.Equal(LocalSchemaBootstrapSupportCap.None, LocalSchemaBootstrapSupport.Cap);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LocalBootstrapOptions.SectionName}:Enabled"] = "false",
                [$"{LocalBootstrapOptions.SectionName}:Phase"] = LocalSchemaBootstrapGuard.PhaseFirstboot,
                [$"{LocalBootstrapOptions.SectionName}:PolicyVersion"] = LocalSchemaBootstrapGuard.PolicyVersion.ToString()
            })
            .Build();

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapGuard.RejectUnsupportedEffectiveRequest(configuration, new TestHostEnvironment(Environments.Development)));

        Assert.Equal(LocalSchemaBootstrapGuard.UnsupportedMessage, exception.Message);
    }

    [Fact]
    public void NormalV1AndProductionMalformedInputsStayIgnoredByExistingStartupGuard()
    {
        var configuration = new ConfigurationManager
        {
            [$"{LocalBootstrapOptions.SectionName}:Enabled"] = "not-bool",
            [$"{LocalBootstrapOptions.SectionName}:Phase"] = "not-a-schema-phase",
            [$"{LocalBootstrapOptions.SectionName}:PolicyVersion"] = "not-an-int",
            [$"{LocalBootstrapOptions.SectionName}:SeedFictionalContent"] = "not-bool"
        };
        var environment = new TestHostEnvironment(Environments.Production);

        LocalSchemaBootstrapGuard.RejectUnsupportedEffectiveRequest(configuration, environment);
        var result = LocalBootstrapGuard.ValidateStartupConfiguration(configuration, environment);

        Assert.False(result.IsDevelopment);
        Assert.False(result.BootstrapEnabled);
    }

    private static IConfiguration BuildConfiguration(string phase, IDictionary<string, string?>? overrides = null)
    {
        var values = BaseValues(phase);
        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                if (pair.Value is null)
                {
                    values.Remove(pair.Key);
                }
                else
                {
                    values[pair.Key] = pair.Value;
                }
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static Dictionary<string, string?> BaseValues(string phase)
    {
        var isFirstboot = phase == LocalSchemaBootstrapGuard.PhaseFirstboot;
        var values = new Dictionary<string, string?>
        {
            ["SGFDevs:LocalBootstrap:Enabled"] = "true",
            ["SGFDevs:LocalBootstrap:CreatedBy"] = LocalSchemaBootstrapPolicyValidator.RequiredCreatedBy,
            ["SGFDevs:LocalBootstrap:PolicyVersion"] = "2",
            ["SGFDevs:LocalBootstrap:Phase"] = phase,
            ["SGFDevs:LocalBootstrap:LogicalDbId"] = "schema-v2-abcdefghijkl",
            ["SGFDevs:LocalBootstrap:SchemaSourceOptIn"] = "true",
            ["SGFDevs:LocalBootstrap:SchemaSourceManifestHash"] = ManifestHash,
            ["SGFDevs:LocalBootstrap:SchemaImportOnFirstBoot"] = isFirstboot ? "true" : "false",
            ["SGFDevs:LocalBootstrap:SeedFictionalContent"] = "false",
            ["Umbraco:CMS:Unattended:InstallUnattended"] = isFirstboot ? "true" : "false",
            ["Umbraco:CMS:Unattended:UpgradeUnattended"] = "false",
            ["Umbraco:CMS:Unattended:PackageMigrationsUnattended"] = "false",
            ["Umbraco:CMS:ModelsBuilder:ModelsMode"] = isFirstboot ? "Nothing" : "SourceCodeManual",
            ["Umbraco:CMS:Global:UseHttps"] = isFirstboot ? "true" : "false",
            ["uSync:Settings:DefaultSet"] = "Default",
            ["uSync:Settings:ImportOnFirstBoot"] = isFirstboot ? "true" : "false",
            ["uSync:Settings:FirstBootGroup"] = isFirstboot ? "Settings" : "",
            ["uSync:Settings:ImportAtStartup"] = "None",
            ["uSync:Settings:ExportAtStartup"] = "None",
            ["uSync:Settings:ExportOnSave"] = "None",
            ["uSync:Sets:Default:Enabled"] = "true",
            ["uSync:Sets:Default:HandlerDefaults:Enabled"] = "false",
            ["uSync:Sets:Default:HandlerDefaults:Actions:0"] = "Import"
        };

        if (isFirstboot)
        {
            values["Umbraco:CMS:Runtime:Mode"] = "Production";
            values["Umbraco:CMS:WebRouting:UmbracoApplicationUrl"] = "https://127.0.0.1:55443/";
        }
        else
        {
            values["Umbraco:CMS:Runtime:Mode"] = "Development";
            values["Umbraco:CMS:WebRouting:UmbracoApplicationUrl"] = "";
        }

        for (var index = 0; index < LocalSchemaBootstrapPolicyValidator.DeniedHandlers.Count; index++)
        {
            var alias = LocalSchemaBootstrapPolicyValidator.DeniedHandlers[index];
            values[$"uSync:Sets:Default:DisabledHandlers:{index}"] = alias;
            values[$"uSync:Sets:Default:Handlers:{alias}:Enabled"] = "false";
        }

        foreach (var alias in LocalSchemaBootstrapPolicyValidator.AllowedHandlers)
        {
            values[$"uSync:Sets:Default:Handlers:{alias}:Enabled"] = "true";
        }

        values["uSync:Sets:Default:Handlers:RelationTypeHandler:Group"] = "Settings";

        return values;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "SgfDevs.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
