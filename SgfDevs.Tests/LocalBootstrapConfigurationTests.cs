using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SgfDevs.Dev.LocalBootstrap;
using Xunit;

namespace SgfDevs.Tests;

public sealed class LocalBootstrapConfigurationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrackedConfiguration_DoesNotOptInToLocalBootstrapOrFictionalSeed(bool development)
    {
        var configuration = LoadConfiguration(development);
        var options = configuration.GetSection(LocalBootstrapOptions.SectionName).Get<LocalBootstrapOptions>() ?? new LocalBootstrapOptions();

        Assert.False(options.Enabled);
        Assert.False(options.SeedFictionalContent);
    }

    [Fact]
    public void ProductionConfiguration_DoesNotForceUsyncStartupSettingsOff()
    {
        var configuration = LoadConfiguration(development: false);
        var settings = configuration.GetSection("uSync:Settings");

        Assert.Null(settings["ImportOnFirstBoot"]);
        Assert.Null(settings["FirstBootGroup"]);
        Assert.Null(settings["ImportAtStartup"]);
        Assert.Null(settings["ExportAtStartup"]);
        Assert.Null(settings["ExportOnSave"]);
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.Development.json")]
    public void TrackedConfiguration_DoesNotContainLocalUnattendedInstallCredentials(string fileName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectDirectory, fileName)));
        Assert.False(HasProperty(document.RootElement, "UnattendedUserName"));
        Assert.False(HasProperty(document.RootElement, "UnattendedUserEmail"));
        Assert.False(HasProperty(document.RootElement, "UnattendedUserPassword"));
        Assert.False(HasInstallUnattendedEnabled(document.RootElement));
    }

    [Fact]
    public void LocalBootstrapLoader_IgnoresExplicitConfigOutsideDevelopment()
    {
        using var temp = TempAppRoot.Create();
        var configPath = temp.CreateLocalConfig();
        var configuration = new ConfigurationManager();
        configuration[LocalBootstrapConfigurationLoader.ConfigPathEnvironmentVariable] = configPath;

        var loaded = LocalBootstrapConfigurationLoader.AddLocalBootstrapConfiguration(
            configuration,
            new TestHostEnvironment(Environments.Production, Path.Combine(temp.Path, "SgfDevs")));

        Assert.False(loaded);
        Assert.Null(configuration.GetConnectionString("umbracoDbDSN"));
    }

    [Fact]
    public void LocalBootstrapLoader_LoadsDevelopmentConfigOnlyFromGuardedDirectory()
    {
        using var temp = TempAppRoot.Create();
        var configPath = temp.CreateLocalConfig();
        var configuration = new ConfigurationManager();
        configuration[LocalBootstrapConfigurationLoader.ConfigPathEnvironmentVariable] = configPath;

        var loaded = LocalBootstrapConfigurationLoader.AddLocalBootstrapConfiguration(
            configuration,
            new TestHostEnvironment(Environments.Development, Path.Combine(temp.Path, "SgfDevs")));

        Assert.True(loaded);
        Assert.True(configuration.GetValue<bool>("SGFDevs:LocalBootstrap:Enabled"));
        Assert.Equal("Data Source=test", configuration.GetConnectionString("umbracoDbDSN"));
    }

    [Fact]
    public void LocalBootstrapLoader_RejectsConfigOutsideBootstrapDirectory()
    {
        using var temp = TempAppRoot.Create();
        var appRoot = Path.Combine(temp.Path, "SgfDevs");
        Directory.CreateDirectory(appRoot);
        var outsidePath = Path.Combine(temp.Path, "local-bootstrap.appsettings.json");
        File.WriteAllText(outsidePath, "{}");
        var configuration = new ConfigurationManager();
        configuration[LocalBootstrapConfigurationLoader.ConfigPathEnvironmentVariable] = outsidePath;

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalBootstrapConfigurationLoader.AddLocalBootstrapConfiguration(
                configuration,
                new TestHostEnvironment(Environments.Development, appRoot)));

        Assert.Contains("must point under", exception.Message);
    }

    [Fact]
    public void Program_ValidatesLocalBootstrapBeforeUmbracoCanBuildOrBoot()
    {
        var program = File.ReadAllText(Path.Combine(ProjectDirectory, "Program.cs"));

        var loaderIndex = program.IndexOf("LocalBootstrapConfigurationLoader.AddLocalBootstrapConfiguration", StringComparison.Ordinal);
        var guardIndex = program.IndexOf("LocalBootstrapGuard.ValidateStartupConfiguration", StringComparison.Ordinal);
        var createBuilderIndex = program.IndexOf("builder.CreateUmbracoBuilder()", StringComparison.Ordinal);
        var effectivePolicyIndex = program.IndexOf("LocalBootstrapEffectivePolicyValidator.Validate", StringComparison.Ordinal);
        var sentryIndex = program.IndexOf("builder.WebHost.UseSentry()", StringComparison.Ordinal);
        var telemetryGuardIndex = program.IndexOf("LocalBootstrapTelemetryGuard.RemoveTelemetryJob", StringComparison.Ordinal);
        var buildIndex = program.IndexOf("umbracoBuilder.Build()", StringComparison.Ordinal);
        var bootIndex = program.IndexOf("app.BootUmbracoAsync()", StringComparison.Ordinal);

        Assert.True(loaderIndex >= 0, "Program.cs must load explicit local bootstrap config before the guard.");
        Assert.True(guardIndex >= 0, "Program.cs must call the local bootstrap guard.");
        Assert.True(loaderIndex < guardIndex, "The local bootstrap config loader must run before the DB guard.");
        Assert.True(guardIndex < effectivePolicyIndex, "The DB guard must run before complete effective-policy validation.");
        Assert.True(effectivePolicyIndex < sentryIndex, "Complete effective-policy validation must run before Sentry registration.");
        Assert.True(effectivePolicyIndex < createBuilderIndex, "Complete effective-policy validation must run before CreateUmbracoBuilder.");
        Assert.True(telemetryGuardIndex > createBuilderIndex, "The local telemetry guard must run after Umbraco registers background jobs.");
        Assert.True(telemetryGuardIndex < buildIndex, "The local telemetry guard must run before UmbracoBuilder.Build.");
        Assert.True(effectivePolicyIndex < buildIndex, "Complete effective-policy validation must run before UmbracoBuilder.Build.");
        Assert.True(effectivePolicyIndex < bootIndex, "Complete effective-policy validation must run before BootUmbracoAsync.");
    }

    private static IConfiguration LoadConfiguration(bool development)
    {
        var builder = new ConfigurationBuilder().AddJsonFile(Path.Combine(ProjectDirectory, "appsettings.json"));
        if (development)
        {
            builder.AddJsonFile(Path.Combine(ProjectDirectory, "appsettings.Development.json"));
        }

        return builder.Build();
    }

    private static bool HasProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase) || HasProperty(property.Value, propertyName))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (HasProperty(item, propertyName))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasInstallUnattendedEnabled(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, "InstallUnattended", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind is JsonValueKind.True)
                {
                    return true;
                }

                if (HasInstallUnattendedEnabled(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind is JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (HasInstallUnattendedEnabled(item))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private sealed class TempAppRoot : IDisposable
    {
        private TempAppRoot(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempAppRoot Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sgf-local-bootstrap-loader-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempAppRoot(path);
        }

        public string CreateLocalConfig()
        {
            var configDirectory = System.IO.Path.Combine(Path, "SgfDevs", "umbraco", "Data", "local-bootstrap");
            Directory.CreateDirectory(configDirectory);
            var configPath = System.IO.Path.Combine(configDirectory, LocalBootstrapConfigurationLoader.ConfigFileName);
            File.WriteAllText(configPath, """
            {
              "SGFDevs": { "LocalBootstrap": { "Enabled": true } },
              "ConnectionStrings": { "umbracoDbDSN": "Data Source=test" }
            }
            """);
            return configPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class TestHostEnvironment(string environmentName, string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "SgfDevs.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string ProjectDirectory
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "SgfDevs");
                if (File.Exists(Path.Combine(candidate, "SgfDevs.csproj")))
                {
                    return candidate;
                }
            }

            throw new DirectoryNotFoundException("Cannot locate the SgfDevs source configuration.");
        }
    }
}
