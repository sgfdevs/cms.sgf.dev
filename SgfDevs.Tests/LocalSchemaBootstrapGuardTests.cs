using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SgfDevs.Dev.LocalBootstrap;
using Xunit;

namespace SgfDevs.Tests;

public sealed class LocalSchemaBootstrapGuardTests
{
    private const string SafeId = "abcdefghijkl";

    [Fact]
    public void SupportCap_RemainsNone()
    {
        Assert.Equal(LocalSchemaBootstrapSupportCap.None, LocalSchemaBootstrapSupport.Cap);
    }

    [Theory]
    [InlineData("local-bootstrap-schema-v2-abcdefghijkl.firstboot.appsettings.json", LocalSchemaBootstrapPhase.Firstboot)]
    [InlineData("local-bootstrap-schema-v2-abcdefghijkl.reuse.appsettings.json", LocalSchemaBootstrapPhase.Reuse)]
    public void ValidateProfileFileName_AcceptsManagedBasenames(string fileName, LocalSchemaBootstrapPhase phase)
    {
        var profile = LocalSchemaBootstrapGuard.ValidateProfileFileName(fileName);

        Assert.Equal(phase, profile.Phase);
        Assert.Equal(SafeId, profile.SafeId);
        Assert.Equal(fileName, profile.FileName);
    }

    [Theory]
    [InlineData("local-bootstrap-schema-v2-short.firstboot.appsettings.json")]
    [InlineData("local-bootstrap-schema-v2-ABCDEFGHIJKL.firstboot.appsettings.json")]
    [InlineData("../local-bootstrap-schema-v2-abcdefghijkl.firstboot.appsettings.json")]
    [InlineData("local-bootstrap-schema-v2-abcdefghijkl.appsettings.json")]
    public void ValidateProfileFileName_RejectsUnmanagedBasenames(string fileName)
    {
        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapGuard.ValidateProfileFileName(fileName));
    }

    [Fact]
    public void RejectUnsupportedConfigPath_StopsManagedProfileBeforeLoadingIt()
    {
        using var temp = TempAppRoot.Create();
        var configPath = Path.Combine(
            temp.LocalBootstrapDirectory,
            LocalSchemaBootstrapGuard.FirstbootProfileFileName(SafeId));
        var configuration = new ConfigurationManager
        {
            [LocalBootstrapConfigurationLoader.ConfigPathEnvironmentVariable] = configPath
        };

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapGuard.RejectUnsupportedConfigPath(configuration, temp.DevelopmentEnvironment));

        Assert.Equal(LocalSchemaBootstrapGuard.UnsupportedMessage, exception.Message);
        Assert.False(File.Exists(configPath));
    }

    [Fact]
    public void Loader_RejectsManagedProfileBasenameWithoutLoadingIt()
    {
        using var temp = TempAppRoot.Create();
        var configPath = Path.Combine(
            temp.LocalBootstrapDirectory,
            LocalSchemaBootstrapGuard.ReuseProfileFileName(SafeId));
        File.WriteAllText(configPath, "{\"SGFDevs\":{\"LocalBootstrap\":{\"Phase\":\"schema-reuse\"}}}");
        var configuration = new ConfigurationManager
        {
            [LocalBootstrapConfigurationLoader.ConfigPathEnvironmentVariable] = configPath
        };

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalBootstrapConfigurationLoader.AddLocalBootstrapConfiguration(configuration, temp.DevelopmentEnvironment));

        Assert.Equal(LocalSchemaBootstrapGuard.UnsupportedMessage, exception.Message);
        Assert.Null(configuration[$"{LocalBootstrapOptions.SectionName}:Phase"]);
    }

    [Theory]
    [InlineData(LocalSchemaBootstrapGuard.PhaseFirstboot)]
    [InlineData(LocalSchemaBootstrapGuard.PhaseReuse)]
    public void RejectUnsupportedEffectiveRequest_RejectsSchemaPhaseEvenWhenDisabled(string phase)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LocalBootstrapOptions.SectionName}:Enabled"] = "false",
                [$"{LocalBootstrapOptions.SectionName}:Phase"] = phase
            })
            .Build();

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapGuard.RejectUnsupportedEffectiveRequest(configuration, DevelopmentEnvironment()));

        Assert.Equal(LocalSchemaBootstrapGuard.UnsupportedMessage, exception.Message);
    }

    [Fact]
    public void RejectUnsupportedEffectiveRequest_RejectsSchemaPolicyVersionFromOverrides()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LocalBootstrapOptions.SectionName}:Enabled"] = "false",
                [$"{LocalBootstrapOptions.SectionName}:PolicyVersion"] = LocalSchemaBootstrapGuard.PolicyVersion.ToString()
            })
            .Build();

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapGuard.RejectUnsupportedEffectiveRequest(configuration, DevelopmentEnvironment()));

        Assert.Equal(LocalSchemaBootstrapGuard.UnsupportedMessage, exception.Message);
    }

    [Fact]
    public void RejectUnsupportedEffectiveRequest_KeepsOrdinaryDisabledDevelopmentBehavior()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{LocalBootstrapOptions.SectionName}:Enabled"] = "false",
                [$"{LocalBootstrapOptions.SectionName}:Phase"] = "not-a-schema-phase",
                [$"{LocalBootstrapOptions.SectionName}:PolicyVersion"] = "not-an-int",
                [$"{LocalBootstrapOptions.SectionName}:SeedFictionalContent"] = "not-bool"
            })
            .Build();

        LocalSchemaBootstrapGuard.RejectUnsupportedEffectiveRequest(configuration, DevelopmentEnvironment());
        var result = LocalBootstrapGuard.ValidateStartupConfiguration(configuration, DevelopmentEnvironment());

        Assert.False(result.BootstrapEnabled);
    }

    [Fact]
    public void SchemaRequests_AreIgnoredOutsideDevelopmentBeforeParsingOrFileChecks()
    {
        var configuration = new ConfigurationManager
        {
            [LocalBootstrapConfigurationLoader.ConfigPathEnvironmentVariable] = "/not/a/real/local-bootstrap-schema-v2-abcdefghijkl.firstboot.appsettings.json",
            [$"{LocalBootstrapOptions.SectionName}:Enabled"] = "not-bool",
            [$"{LocalBootstrapOptions.SectionName}:Phase"] = LocalSchemaBootstrapGuard.PhaseFirstboot,
            [$"{LocalBootstrapOptions.SectionName}:PolicyVersion"] = LocalSchemaBootstrapGuard.PolicyVersion.ToString(),
            [$"{LocalBootstrapOptions.SectionName}:SeedFictionalContent"] = "not-bool"
        };
        var environment = new TestHostEnvironment(Environments.Production, Directory.GetCurrentDirectory());

        LocalSchemaBootstrapGuard.RejectUnsupportedConfigPath(configuration, environment);
        Assert.False(LocalBootstrapConfigurationLoader.AddLocalBootstrapConfiguration(configuration, environment));
        LocalSchemaBootstrapGuard.RejectUnsupportedEffectiveRequest(configuration, environment);
        var result = LocalBootstrapGuard.ValidateStartupConfiguration(configuration, environment);

        Assert.False(result.IsDevelopment);
        Assert.False(result.BootstrapEnabled);
    }

    [Fact]
    public void ValidateProviderAndConnectionString_AcceptsManagedSqliteFileDsn()
    {
        using var temp = TempAppRoot.Create();
        var databasePath = Path.Combine(temp.LocalBootstrapDirectory, LocalSchemaBootstrapGuard.DatabaseFileName(SafeId));

        LocalSchemaBootstrapGuard.ValidateProviderAndConnectionString(
            "microsoft.data.sqlite",
            $"Data Source={databasePath};Cache=Shared",
            temp.AppRoot);
    }

    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=file:local-bootstrap-schema-v2-abcdefghijkl.sqlite")]
    [InlineData("Data Source=../local-bootstrap-schema-v2-abcdefghijkl.sqlite")]
    [InlineData("Data Source=local-bootstrap-schema-v2-abcdefghijkl.sqlite;Mode=Memory")]
    public void ValidateProviderAndConnectionString_RejectsUnsafeDsn(string connectionString)
    {
        using var temp = TempAppRoot.Create();

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapGuard.ValidateProviderAndConnectionString(
                LocalBootstrapGuard.RequiredProviderName,
                connectionString,
                temp.AppRoot));
    }

    [Fact]
    public void ValidateFirstbootFamilyAbsent_RejectsMainAndSidecars()
    {
        using var temp = TempAppRoot.Create();
        var databasePath = Path.Combine(temp.LocalBootstrapDirectory, LocalSchemaBootstrapGuard.DatabaseFileName(SafeId));
        File.WriteAllText(databasePath + "-wal", "sidecar");

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapGuard.ValidateFirstbootFamilyAbsent(databasePath));

        Assert.Contains("fresh database family", exception.Message);
    }

    [Fact]
    public void ValidateReuseFamilyMetadata_AcceptsRegularMainAndSidecars()
    {
        using var temp = TempAppRoot.Create();
        var databasePath = Path.Combine(temp.LocalBootstrapDirectory, LocalSchemaBootstrapGuard.DatabaseFileName(SafeId));
        File.WriteAllText(databasePath, "not opened as sqlite");
        File.WriteAllText(databasePath + "-shm", "sidecar");

        LocalSchemaBootstrapGuard.ValidateReuseFamilyMetadata(databasePath);
    }

    [Fact]
    public void ValidateReuseFamilyMetadata_RejectsUnknownSidecar()
    {
        using var temp = TempAppRoot.Create();
        var databasePath = Path.Combine(temp.LocalBootstrapDirectory, LocalSchemaBootstrapGuard.DatabaseFileName(SafeId));
        File.WriteAllText(databasePath, "not opened as sqlite");
        File.WriteAllText(databasePath + "-unknown", "sidecar");

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapGuard.ValidateReuseFamilyMetadata(databasePath));

        Assert.Contains("unknown sidecar", exception.Message);
    }

    [Fact]
    public void ValidateReuseFamilyMetadata_RejectsSidecarSymlinkWhenSupported()
    {
        using var temp = TempAppRoot.Create();
        var databasePath = Path.Combine(temp.LocalBootstrapDirectory, LocalSchemaBootstrapGuard.DatabaseFileName(SafeId));
        File.WriteAllText(databasePath, "not opened as sqlite");
        var target = Path.Combine(temp.LocalBootstrapDirectory, "target");
        File.WriteAllText(target, "target");
        var sidecar = databasePath + "-journal";

        try
        {
            File.CreateSymbolicLink(sidecar, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.Throws<LocalBootstrapConfigurationException>(() =>
            LocalSchemaBootstrapGuard.ValidateReuseFamilyMetadata(databasePath));
    }

    private static TestHostEnvironment DevelopmentEnvironment() =>
        new(Environments.Development, Directory.GetCurrentDirectory());

    private sealed class TempAppRoot : IDisposable
    {
        private TempAppRoot(string path)
        {
            Path = path;
            AppRoot = System.IO.Path.Combine(path, "SgfDevs");
            LocalBootstrapDirectory = System.IO.Path.Combine(AppRoot, "umbraco", "Data", "local-bootstrap");
            Directory.CreateDirectory(LocalBootstrapDirectory);
            DevelopmentEnvironment = new TestHostEnvironment(Environments.Development, AppRoot);
        }

        public string Path { get; }

        public string AppRoot { get; }

        public string LocalBootstrapDirectory { get; }

        public TestHostEnvironment DevelopmentEnvironment { get; }

        public static TempAppRoot Create()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sgf-schema-guard-tests", Guid.NewGuid().ToString("N"));
            return new TempAppRoot(root);
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
}
