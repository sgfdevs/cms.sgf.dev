using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using SgfDevs.Dev.LocalBootstrap;
using Xunit;

namespace SgfDevs.Tests;

public sealed class LocalBootstrapGuardTests
{
    [Fact]
    public void Validate_DisablesBootstrapOutsideDevelopmentEvenWhenFlagsAreSet()
    {
        using var temp = TempAppRoot.Create();

        var result = Validate(
            temp.Path,
            Environments.Production,
            enabled: true,
            seed: true,
            providerName: "System.Data.SqlClient",
            connectionString: "Server=prod;Database=umbraco");

        Assert.False(result.IsDevelopment);
        Assert.False(result.BootstrapEnabled);
        Assert.False(result.SeedFictionalContentEnabled);
        Assert.Null(result.DatabasePath);
    }

    [Fact]
    public void Validate_DisabledDevelopmentBootstrapDoesNotInspectDatabaseSettings()
    {
        using var temp = TempAppRoot.Create();

        var result = Validate(
            temp.Path,
            Environments.Development,
            enabled: false,
            seed: false,
            providerName: "System.Data.SqlClient",
            connectionString: "Server=prod;Database=umbraco");

        Assert.True(result.IsDevelopment);
        Assert.False(result.BootstrapEnabled);
        Assert.Null(result.DatabasePath);
    }

    [Fact]
    public void Validate_DisabledDevelopmentBootstrapIgnoresSeedFlag()
    {
        using var temp = TempAppRoot.Create();

        var result = Validate(
            temp.Path,
            Environments.Development,
            enabled: false,
            seed: true,
            providerName: LocalBootstrapGuard.RequiredProviderName,
            connectionString: SafeConnectionString(temp.Path));

        Assert.True(result.IsDevelopment);
        Assert.False(result.BootstrapEnabled);
        Assert.False(result.SeedFictionalContentEnabled);
        Assert.Null(result.DatabasePath);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("QA")]
    public void ValidateStartupConfiguration_IgnoresMalformedLocalBootstrapFlagsOutsideDevelopment(string environmentName)
    {
        using var temp = TempAppRoot.Create();

        var result = ValidateStartup(temp.Path, environmentName, new Dictionary<string, string?>
        {
            ["SGFDevs:LocalBootstrap:Enabled"] = "not-bool",
            ["SGFDevs:LocalBootstrap:SeedFictionalContent"] = "not-bool",
            ["ConnectionStrings:umbracoDbDSN_ProviderName"] = "System.Data.SqlClient",
            ["ConnectionStrings:umbracoDbDSN"] = "Server=prod;Database=umbraco"
        });

        Assert.False(result.IsDevelopment);
        Assert.False(result.BootstrapEnabled);
        Assert.False(result.SeedFictionalContentEnabled);
        Assert.Null(result.DatabasePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void ValidateStartupConfiguration_DisabledDevelopmentIgnoresMalformedSeedFlag(string? enabledValue)
    {
        using var temp = TempAppRoot.Create();
        var values = new Dictionary<string, string?>
        {
            ["SGFDevs:LocalBootstrap:SeedFictionalContent"] = "not-bool",
            ["ConnectionStrings:umbracoDbDSN_ProviderName"] = "System.Data.SqlClient",
            ["ConnectionStrings:umbracoDbDSN"] = "Server=prod;Database=umbraco"
        };
        if (enabledValue is not null)
        {
            values["SGFDevs:LocalBootstrap:Enabled"] = enabledValue;
        }

        var result = ValidateStartup(temp.Path, Environments.Development, values);

        Assert.True(result.IsDevelopment);
        Assert.False(result.BootstrapEnabled);
        Assert.False(result.SeedFictionalContentEnabled);
        Assert.Null(result.DatabasePath);
    }

    [Fact]
    public void ValidateStartupConfiguration_RejectsMalformedEnabledFlagInDevelopment()
    {
        using var temp = TempAppRoot.Create();

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() => ValidateStartup(
            temp.Path,
            Environments.Development,
            new Dictionary<string, string?>
            {
                ["SGFDevs:LocalBootstrap:Enabled"] = "not-bool",
                ["ConnectionStrings:umbracoDbDSN_ProviderName"] = LocalBootstrapGuard.RequiredProviderName,
                ["ConnectionStrings:umbracoDbDSN"] = SafeConnectionString(temp.Path)
            }));

        Assert.Contains("SGFDevs:LocalBootstrap:Enabled", exception.Message);
    }

    [Fact]
    public void ValidateStartupConfiguration_RejectsMalformedSeedFlagOnlyAfterDevelopmentOptIn()
    {
        using var temp = TempAppRoot.Create();

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() => ValidateStartup(
            temp.Path,
            Environments.Development,
            new Dictionary<string, string?>
            {
                ["SGFDevs:LocalBootstrap:Enabled"] = "true",
                ["SGFDevs:LocalBootstrap:SeedFictionalContent"] = "not-bool",
                ["ConnectionStrings:umbracoDbDSN_ProviderName"] = LocalBootstrapGuard.RequiredProviderName,
                ["ConnectionStrings:umbracoDbDSN"] = SafeConnectionString(temp.Path)
            }));

        Assert.Contains("SGFDevs:LocalBootstrap:SeedFictionalContent", exception.Message);
    }

    [Theory]
    [InlineData("Microsoft.Data.Sqlite")]
    [InlineData("microsoft.data.sqlite")]
    [InlineData("MICROSOFT.DATA.SQLITE")]
    public void Validate_AcceptsSqliteProviderWithExactCaseInsensitiveName(string providerName)
    {
        using var temp = TempAppRoot.Create();

        var result = Validate(
            temp.Path,
            Environments.Development,
            enabled: true,
            seed: false,
            providerName: providerName,
            connectionString: SafeConnectionString(temp.Path));

        Assert.True(result.BootstrapEnabled);
        Assert.EndsWith(Path.Combine("umbraco", "Data", "local-bootstrap", "local-bootstrap.sqlite"), result.DatabasePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Microsoft.Data.SqlClient")]
    [InlineData("Npgsql")]
    [InlineData("MySqlConnector")]
    [InlineData("Microsoft.Data.Sqlite.Production")]
    public void Validate_RejectsNonSqliteProviders(string providerName)
    {
        using var temp = TempAppRoot.Create();

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() => Validate(
            temp.Path,
            Environments.Development,
            enabled: true,
            seed: false,
            providerName: providerName,
            connectionString: SafeConnectionString(temp.Path)));

        Assert.Contains(LocalBootstrapGuard.RequiredProviderName, exception.Message);
    }

    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=local-bootstrap;Mode=Memory")]
    [InlineData("Data Source=file:local-bootstrap.sqlite")]
    [InlineData("Data Source=file:///tmp/local-bootstrap.sqlite")]
    [InlineData("Data Source=https://example.com/local-bootstrap.sqlite")]
    [InlineData("Data Source=//server/share/local-bootstrap.sqlite")]
    [InlineData("Data Source=\\\\server\\share\\local-bootstrap.sqlite")]
    public void Validate_RejectsMemoryUriAndNetworkDataSources(string connectionString)
    {
        using var temp = TempAppRoot.Create();

        Assert.Throws<LocalBootstrapConfigurationException>(() => Validate(
            temp.Path,
            Environments.Development,
            enabled: true,
            seed: false,
            providerName: LocalBootstrapGuard.RequiredProviderName,
            connectionString: connectionString));
    }

    [Theory]
    [InlineData("Data Source=../umbraco/Data/local-bootstrap/local-bootstrap.sqlite")]
    [InlineData("Data Source=umbraco/Data/local-bootstrap/../local-bootstrap.sqlite")]
    public void Validate_RejectsTraversalSegments(string connectionString)
    {
        using var temp = TempAppRoot.Create();

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() => Validate(
            temp.Path,
            Environments.Development,
            enabled: true,
            seed: false,
            providerName: LocalBootstrapGuard.RequiredProviderName,
            connectionString: connectionString));

        Assert.Contains("traversal", exception.Message);
    }

    [Fact]
    public void Validate_RejectsAbsoluteDatabasePathOutsideAllowedDirectory()
    {
        using var temp = TempAppRoot.Create();
        using var outside = TempAppRoot.Create();

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() => Validate(
            temp.Path,
            Environments.Development,
            enabled: true,
            seed: false,
            providerName: LocalBootstrapGuard.RequiredProviderName,
            connectionString: $"Data Source={Path.Combine(outside.Path, "local-bootstrap.sqlite")}"));

        Assert.Contains("must be under", exception.Message);
    }

    [Fact]
    public void Validate_RejectsRelativeDatabasePathOutsideAllowedDirectory()
    {
        using var temp = TempAppRoot.Create();

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() => Validate(
            temp.Path,
            Environments.Development,
            enabled: true,
            seed: false,
            providerName: LocalBootstrapGuard.RequiredProviderName,
            connectionString: "Data Source=local-bootstrap.sqlite"));

        Assert.Contains("must be under", exception.Message);
    }

    [Fact]
    public void Validate_RejectsExistingNonBootstrapDatabaseInAllowedDirectory()
    {
        using var temp = TempAppRoot.Create();
        var databasePath = Path.Combine(temp.Path, "umbraco", "Data", "local-bootstrap", "umbraco.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        File.WriteAllText(databasePath, "not ours");

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() => Validate(
            temp.Path,
            Environments.Development,
            enabled: true,
            seed: false,
            providerName: LocalBootstrapGuard.RequiredProviderName,
            connectionString: $"Data Source={databasePath}"));

        Assert.Contains("local-bootstrap", exception.Message);
    }

    [Fact]
    public void Validate_AllowsExistingBootstrapDatabaseInAllowedDirectory()
    {
        using var temp = TempAppRoot.Create();
        var databasePath = Path.Combine(temp.Path, "umbraco", "Data", "local-bootstrap", "local-bootstrap.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        File.WriteAllText(databasePath, "existing local bootstrap database");

        var result = Validate(
            temp.Path,
            Environments.Development,
            enabled: true,
            seed: false,
            providerName: LocalBootstrapGuard.RequiredProviderName,
            connectionString: $"Data Source={databasePath}");

        Assert.Equal(Path.GetFullPath(databasePath), result.DatabasePath);
        Assert.Equal("existing local bootstrap database", File.ReadAllText(databasePath));
    }

    [Fact]
    public void Validate_DoesNotCreateDirectoriesOrDatabaseFiles()
    {
        using var temp = TempAppRoot.Create();
        var directory = Path.Combine(temp.Path, "umbraco", "Data", "local-bootstrap");
        var databasePath = Path.Combine(directory, "local-bootstrap.sqlite");

        var result = Validate(
            temp.Path,
            Environments.Development,
            enabled: true,
            seed: false,
            providerName: LocalBootstrapGuard.RequiredProviderName,
            connectionString: $"Data Source={databasePath}");

        Assert.Equal(Path.GetFullPath(databasePath), result.DatabasePath);
        Assert.False(Directory.Exists(directory));
        Assert.False(File.Exists(databasePath));
    }

    [Fact]
    public void Validate_RejectsSymlinkedBootstrapDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TempAppRoot.Create();
        using var outside = TempAppRoot.Create();
        var dataDirectory = Path.Combine(temp.Path, "umbraco", "Data");
        Directory.CreateDirectory(dataDirectory);
        var bootstrapDirectory = Path.Combine(dataDirectory, "local-bootstrap");
        Directory.CreateSymbolicLink(bootstrapDirectory, outside.Path);

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() => Validate(
            temp.Path,
            Environments.Development,
            enabled: true,
            seed: false,
            providerName: LocalBootstrapGuard.RequiredProviderName,
            connectionString: $"Data Source={Path.Combine(bootstrapDirectory, "local-bootstrap.sqlite")}"));

        Assert.Contains("symbolic links", exception.Message);
    }

    [Fact]
    public void Validate_RejectsWindowsJunctionBootstrapDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TempAppRoot.Create();
        using var outside = TempAppRoot.Create();
        var dataDirectory = Path.Combine(temp.Path, "umbraco", "Data");
        Directory.CreateDirectory(dataDirectory);
        var bootstrapDirectory = Path.Combine(dataDirectory, "local-bootstrap");
        CreateJunction(bootstrapDirectory, outside.Path);

        try
        {
            Assert.True(new DirectoryInfo(bootstrapDirectory).Attributes.HasFlag(FileAttributes.ReparsePoint));

            var exception = Assert.Throws<LocalBootstrapConfigurationException>(() => Validate(
                temp.Path,
                Environments.Development,
                enabled: true,
                seed: false,
                providerName: LocalBootstrapGuard.RequiredProviderName,
                connectionString: $"Data Source={Path.Combine(bootstrapDirectory, "local-bootstrap.sqlite")}"));

            Assert.Matches("reparse|symbolic", exception.Message.ToLowerInvariant());
        }
        finally
        {
            if (Directory.Exists(bootstrapDirectory))
            {
                Directory.Delete(bootstrapDirectory);
            }
        }
    }

    [Fact]
    public void Validate_RejectsSymlinkedExistingBootstrapDatabase()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TempAppRoot.Create();
        using var outside = TempAppRoot.Create();
        var bootstrapDirectory = Path.Combine(temp.Path, "umbraco", "Data", "local-bootstrap");
        Directory.CreateDirectory(bootstrapDirectory);
        var target = Path.Combine(outside.Path, "local-bootstrap.sqlite");
        File.WriteAllText(target, "outside");
        var databasePath = Path.Combine(bootstrapDirectory, "local-bootstrap.sqlite");
        File.CreateSymbolicLink(databasePath, target);

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() => Validate(
            temp.Path,
            Environments.Development,
            enabled: true,
            seed: false,
            providerName: LocalBootstrapGuard.RequiredProviderName,
            connectionString: $"Data Source={databasePath}"));

        Assert.Contains("symbolic link", exception.Message);
    }

    [Fact]
    public void Validate_RejectsDanglingSymlinkedBootstrapDatabase()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = TempAppRoot.Create();
        var bootstrapDirectory = Path.Combine(temp.Path, "umbraco", "Data", "local-bootstrap");
        Directory.CreateDirectory(bootstrapDirectory);
        var databasePath = Path.Combine(bootstrapDirectory, "local-bootstrap.sqlite");
        File.CreateSymbolicLink(databasePath, Path.Combine(temp.Path, "missing-target.sqlite"));

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() => Validate(
            temp.Path,
            Environments.Development,
            enabled: true,
            seed: false,
            providerName: LocalBootstrapGuard.RequiredProviderName,
            connectionString: $"Data Source={databasePath}"));

        Assert.Contains("symbolic link", exception.Message);
    }

    private static LocalBootstrapGuardResult ValidateStartup(
        string appRoot,
        string environmentName,
        IReadOnlyDictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return LocalBootstrapGuard.ValidateStartupConfiguration(configuration, new TestHostEnvironment
        {
            EnvironmentName = environmentName,
            ContentRootPath = appRoot
        });
    }

    private static LocalBootstrapGuardResult Validate(
        string appRoot,
        string environmentName,
        bool enabled,
        bool seed,
        string? providerName,
        string? connectionString)
    {
        return LocalBootstrapGuard.Validate(new LocalBootstrapGuardContext(
            environmentName,
            appRoot,
            new LocalBootstrapOptions
            {
                Enabled = enabled,
                SeedFictionalContent = seed
            },
            providerName,
            connectionString));
    }

    private static string SafeConnectionString(string appRoot)
    {
        return $"Data Source={Path.Combine(appRoot, "umbraco", "Data", "local-bootstrap", "local-bootstrap.sqlite")}";
    }

    private static void CreateJunction(string junctionPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start mklink.");
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "SgfDevs.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
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
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sgf-local-bootstrap-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempAppRoot(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
