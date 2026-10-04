#nullable enable

using System;
using System.Net;
using Microsoft.Extensions.Configuration;

namespace SgfDevs.Dev.LocalBootstrap;

public static class LocalBootstrapEffectivePolicyValidator
{
    public const string RequiredCreatedBy = "scripts/bootstrap-local-cms.py";
    public const string RequiredS3Endpoint = "http://127.0.0.1:8333";
    public const string RequiredS3Bucket = "sgf-dev-local";
    public const string RequiredS3AccessKey = "sgf-dev-local";
    public const string RequiredS3SecretKey = "sgf-dev-local-password";
    public const string RequiredAwsRegion = "us-east-2";
    public const string RequiredAdminName = "SGF Local Bootstrap Admin";
    public const string RequiredAdminEmail = "local-bootstrap-admin@sgf.dev.invalid";

    private static readonly string[] ForbiddenAwsRoutingKeys =
    [
        "AWS:Profile",
        "AWS:ProfilesLocation",
        "AWS:SessionToken",
        "AWS_PROFILE",
        "AWS_DEFAULT_PROFILE",
        "AWS_SESSION_TOKEN",
        "AWS_WEB_IDENTITY_TOKEN_FILE",
        "AWS_ROLE_ARN",
        "AWS_ROLE_SESSION_NAME",
        "AWS_SHARED_CREDENTIALS_FILE",
        "AWS_CONFIG_FILE"
    ];

    private static readonly string[] ForbiddenProxyKeys =
    [
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "ALL_PROXY",
        "http_proxy",
        "https_proxy",
        "all_proxy"
    ];

    private static readonly string[] ForbiddenTelemetryKeys =
    [
        "Sentry:Dsn",
        "Sentry__Dsn",
        "SENTRY_DSN",
        "SENTRY__DSN",
        "OTEL_EXPORTER_OTLP_ENDPOINT",
        "OTEL_EXPORTER_OTLP_TRACES_ENDPOINT",
        "OTEL_EXPORTER_OTLP_METRICS_ENDPOINT",
        "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT",
        "OTEL_EXPORTER_OTLP_PROTOCOL",
        "OTEL_EXPORTER_OTLP_TRACES_PROTOCOL",
        "OTEL_EXPORTER_OTLP_METRICS_PROTOCOL",
        "OTEL_EXPORTER_OTLP_LOGS_PROTOCOL",
        "OTEL_EXPORTER_OTLP_HEADERS",
        "OTEL_EXPORTER_OTLP_TRACES_HEADERS",
        "OTEL_EXPORTER_OTLP_METRICS_HEADERS",
        "OTEL_EXPORTER_OTLP_LOGS_HEADERS",
        "OTEL_TRACES_EXPORTER",
        "OTEL_METRICS_EXPORTER",
        "OTEL_LOGS_EXPORTER",
        "OpenTelemetry:Exporter:Otlp:Endpoint",
        "OpenTelemetry:Exporter:Otlp:Protocol",
        "OpenTelemetry:Exporter:Otlp:Headers",
        "OpenTelemetry__Exporter__Otlp__Endpoint",
        "OpenTelemetry__Exporter__Otlp__Protocol",
        "OpenTelemetry__Exporter__Otlp__Headers"
    ];

    private static readonly string[] ForbiddenDotNetInstrumentationKeys =
    [
        "DOTNET_STARTUP_HOOKS",
        "DOTNET_ADDITIONAL_DEPS",
        "DOTNET_SHARED_STORE",
        "CORECLR_ENABLE_PROFILING",
        "CORECLR_PROFILER",
        "CORECLR_PROFILER_PATH",
        "CORECLR_PROFILER_PATH_64",
        "CORECLR_PROFILER_PATH_32",
        "COR_ENABLE_PROFILING",
        "COR_PROFILER",
        "COR_PROFILER_PATH",
        "COR_PROFILER_PATH_64",
        "COR_PROFILER_PATH_32"
    ];

    public static void Validate(IConfiguration configuration, LocalBootstrapGuardResult guardResult, bool explicitConfigLoaded = false)
    {
        if (!guardResult.BootstrapEnabled)
        {
            if (explicitConfigLoaded)
            {
                throw new LocalBootstrapConfigurationException(
                    "Explicit local bootstrap config must keep SGFDevs:LocalBootstrap:Enabled true.");
            }

            return;
        }

        var databasePath = guardResult.DatabasePath ??
            throw new LocalBootstrapConfigurationException("Local bootstrap requires a validated database path.");

        RequireValue(configuration, "SGFDevs:LocalBootstrap:CreatedBy", RequiredCreatedBy);
        RequireValue(configuration, "SGFDevs:LocalBootstrap:PolicyVersion", "1");
        RequireFalse(configuration, "SGFDevs:LocalBootstrap:SeedFictionalContent");
        RequireValue(configuration, "ConnectionStrings:umbracoDbDSN", $"Data Source={databasePath};Cache=Shared");
        RequireValue(configuration, "ConnectionStrings:umbracoDbDSN_ProviderName", LocalBootstrapGuard.RequiredProviderName, ignoreCase: true);
        ValidateLoopbackUrls(configuration["urls"] ?? configuration["ASPNETCORE_URLS"]);

        RequireValue(configuration, "AWS:Region", RequiredAwsRegion);
        RequireValue(configuration, "AWS:ServiceURL", RequiredS3Endpoint);
        RequireTrue(configuration, "AWS:ForcePathStyle");
        RequireValue(configuration, "AWS_ACCESS_KEY_ID", RequiredS3AccessKey);
        RequireValue(configuration, "AWS_SECRET_ACCESS_KEY", RequiredS3SecretKey);
        RequireValue(configuration, "AWS_EC2_METADATA_DISABLED", "true", ignoreCase: true);
        foreach (var key in ForbiddenAwsRoutingKeys)
        {
            RequireEmpty(configuration, key);
        }

        foreach (var key in ForbiddenProxyKeys)
        {
            RequireEmpty(configuration, key);
        }

        foreach (var key in ForbiddenTelemetryKeys)
        {
            RequireEmpty(configuration, key);
        }
        RequireNoConfiguredKeyPrefix(configuration, "OTEL_");
        RequireNoConfiguredKeyPrefix(configuration, "OpenTelemetry:");

        foreach (var key in ForbiddenDotNetInstrumentationKeys)
        {
            RequireEmpty(configuration, key);
        }

        RequireValue(configuration, "Umbraco:Storage:AWSS3:Media:BucketName", RequiredS3Bucket);
        RequireValue(configuration, "Umbraco:Storage:AWSS3:Media:Region", RequiredAwsRegion);
        RequireValue(configuration, "Umbraco:Storage:AWSS3:Media:MediaBucketPrefix", "media");
        RequireValue(configuration, "Umbraco:Storage:AWSS3:Media:CacheBucketPrefix", "cache");
        RequireFalse(configuration, "Umbraco:Storage:AWSS3:Media:CacheRetention:Enabled");

        RequireTrue(configuration, "Umbraco:CMS:Unattended:InstallUnattended");
        RequireTrue(configuration, "Umbraco:CMS:Unattended:UpgradeUnattended");
        RequireTrue(configuration, "Umbraco:CMS:Unattended:PackageMigrationsUnattended");
        RequireValue(configuration, "Umbraco:CMS:Unattended:UnattendedUserName", RequiredAdminName);
        RequireValue(configuration, "Umbraco:CMS:Unattended:UnattendedUserEmail", RequiredAdminEmail);
        RequireLocalPassword(configuration["Umbraco:CMS:Unattended:UnattendedUserPassword"]);
        RequireValue(configuration, "Umbraco:CMS:Unattended:UnattendedTelemetryLevel", "Minimal");
        RequirePresent(configuration, "Umbraco:CMS:Imaging:HMACSecretKey");

        RequireFalse(configuration, "SGFDevs:EventSyncEnabled");
        RequireEmpty(configuration, "SGFDevs:NewsletterEndpoint");
        RequireEmpty(configuration, "SGFDevs:NewsletterListId");
        RequireEmpty(configuration, "SGFDevs:Sessionize:BaseUrl");
        RequireEmpty(configuration, "SGFDevs:MeetupApi:BaseUrl");
        RequireEmpty(configuration, "SGFDevs:MeetupApi:ClientId");
        RequireEmpty(configuration, "SGFDevs:MeetupApi:ClientSecret");
        RequireFalse(configuration, "SGFDevs:Site:AnalyticsEnabled");
        RequireFalse(configuration, "SGFDevs:Site:SearchIndexingEnabled");

        RequireFalse(configuration, "uSync:Settings:ImportOnFirstBoot");
        RequireEmpty(configuration, "uSync:Settings:FirstBootGroup");
        RequireValue(configuration, "uSync:Settings:ImportAtStartup", "None");
        RequireValue(configuration, "uSync:Settings:ExportAtStartup", "None");
        RequireValue(configuration, "uSync:Settings:ExportOnSave", "None");
    }

    private static void ValidateLoopbackUrls(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
        {
            throw new LocalBootstrapConfigurationException("Local bootstrap requires a loopback web URL.");
        }

        var values = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0)
        {
            throw new LocalBootstrapConfigurationException("Local bootstrap requires a loopback web URL.");
        }

        foreach (var value in values)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttp ||
                uri.Port is < 1024 or > 65535 ||
                !IPAddress.TryParse(uri.Host, out var address) ||
                !IPAddress.IsLoopback(address))
            {
                throw new LocalBootstrapConfigurationException("Local bootstrap web URL must be an HTTP loopback endpoint.");
            }
        }
    }

    private static void RequireTrue(IConfiguration configuration, string key) => RequireBoolean(configuration, key, expected: true);

    private static void RequireFalse(IConfiguration configuration, string key) => RequireBoolean(configuration, key, expected: false);

    private static void RequireBoolean(IConfiguration configuration, string key, bool expected)
    {
        var value = configuration[key];
        if (!bool.TryParse(value, out var parsed) || parsed != expected)
        {
            throw new LocalBootstrapConfigurationException($"Local bootstrap requires {key} to be {expected.ToString().ToLowerInvariant()}.");
        }
    }

    private static void RequireValue(IConfiguration configuration, string key, string expected, bool ignoreCase = false)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(configuration[key], expected, comparison))
        {
            throw new LocalBootstrapConfigurationException($"Local bootstrap requires {key} to match the generated local policy.");
        }
    }

    private static void RequireEmpty(IConfiguration configuration, string key)
    {
        if (!string.IsNullOrWhiteSpace(configuration[key]))
        {
            throw new LocalBootstrapConfigurationException($"Local bootstrap requires {key} to be empty.");
        }
    }

    private static void RequirePresent(IConfiguration configuration, string key)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
        {
            throw new LocalBootstrapConfigurationException($"Local bootstrap requires {key} in the generated local policy.");
        }
    }

    private static void RequireNoConfiguredKeyPrefix(IConfiguration configuration, string prefix)
    {
        foreach (var pair in configuration.AsEnumerable())
        {
            if (pair.Value is not null && pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new LocalBootstrapConfigurationException($"Local bootstrap requires {pair.Key} to be empty.");
            }
        }
    }

    private static void RequireLocalPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 30)
        {
            throw new LocalBootstrapConfigurationException("Local bootstrap requires a generated local admin password.");
        }
    }
}
