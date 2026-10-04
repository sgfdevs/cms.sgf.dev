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
    public void Validate_SeedFlagRequiresBootstrapOptInInDevelopment()
    {
        using var temp = TempAppRoot.Create();

        var exception = Assert.Throws<LocalBootstrapConfigurationException>(() => Validate(
            temp.Path,
            Environments.Development,
            enabled: false,
            seed: true,
            providerName: LocalBootstrapGuard.RequiredProviderName,
            connectionString: SafeConnectionString(temp.Path)));

        Assert.Contains("SeedFictionalContent", exception.Message);
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
