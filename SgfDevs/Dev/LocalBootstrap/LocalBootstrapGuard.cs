#nullable enable

using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SgfDevs.Dev.LocalBootstrap;

public sealed class LocalBootstrapConfigurationException(string message) : InvalidOperationException(message);

public sealed record LocalBootstrapGuardContext(
    string EnvironmentName,
    string AppRoot,
    LocalBootstrapOptions Options,
    string? ProviderName,
    string? ConnectionString);

public sealed record LocalBootstrapGuardResult(
    bool IsDevelopment,
    bool BootstrapEnabled,
    bool SeedFictionalContentEnabled,
    string AllowedDirectory,
    string? DatabasePath);

public static class LocalBootstrapGuard
{
    public const string RequiredProviderName = "Microsoft.Data.Sqlite";
    public const string DatabaseFileNamePrefix = "local-bootstrap";

    private static readonly string[] AllowedDatabaseExtensions = [".db", ".sqlite", ".sqlite3"];

    public static LocalBootstrapGuardResult ValidateStartupConfiguration(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var isDevelopment = string.Equals(environment.EnvironmentName, Environments.Development, StringComparison.OrdinalIgnoreCase);
        var options = new LocalBootstrapOptions();

        if (isDevelopment)
        {
            var section = configuration.GetSection(LocalBootstrapOptions.SectionName);
            options.Enabled = ParseOptionalBoolean(section, nameof(LocalBootstrapOptions.Enabled), defaultValue: false);
            if (options.Enabled)
            {
                options.SeedFictionalContent = ParseOptionalBoolean(section, nameof(LocalBootstrapOptions.SeedFictionalContent), defaultValue: false);
            }
        }

        return Validate(new LocalBootstrapGuardContext(
            environment.EnvironmentName,
            environment.ContentRootPath,
            options,
            configuration["ConnectionStrings:umbracoDbDSN_ProviderName"],
            configuration.GetConnectionString("umbracoDbDSN")));
    }

    public static LocalBootstrapGuardResult Validate(LocalBootstrapGuardContext context)
    {
        var appRoot = GetFullPath(context.AppRoot, Directory.GetCurrentDirectory());
        var allowedDirectory = GetFullPath(Path.Combine(appRoot, "umbraco", "Data", "local-bootstrap"), appRoot);
        var isDevelopment = string.Equals(context.EnvironmentName, Environments.Development, StringComparison.OrdinalIgnoreCase);

        if (!isDevelopment)
        {
            return new LocalBootstrapGuardResult(false, false, false, allowedDirectory, null);
        }

        if (!context.Options.Enabled)
        {
            return new LocalBootstrapGuardResult(true, false, false, allowedDirectory, null);
        }

        ValidateProviderName(context.ProviderName);
        var databasePath = ValidateConnectionString(context.ConnectionString, appRoot, allowedDirectory);

        return new LocalBootstrapGuardResult(
            true,
            true,
            context.Options.SeedFictionalContent,
            allowedDirectory,
            databasePath);
    }

    private static void ValidateProviderName(string? providerName)
    {
        if (!string.Equals(providerName?.Trim(), RequiredProviderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalBootstrapConfigurationException(
                $"Local bootstrap requires ConnectionStrings:umbracoDbDSN_ProviderName to be {RequiredProviderName}.");
        }
    }

    private static string ValidateConnectionString(string? connectionString, string appRoot, string allowedDirectory)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new LocalBootstrapConfigurationException("Local bootstrap requires ConnectionStrings:umbracoDbDSN.");
        }

        SqliteConnectionStringBuilder builder;
        try
        {
            builder = new SqliteConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new LocalBootstrapConfigurationException("Local bootstrap SQLite connection string is invalid.");
        }

        if (builder.Mode is SqliteOpenMode.Memory)
        {
            throw new LocalBootstrapConfigurationException("Local bootstrap cannot use SQLite memory mode.");
        }

        var dataSource = builder.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            throw new LocalBootstrapConfigurationException("Local bootstrap SQLite connection string must include Data Source.");
        }

        ValidateDataSourceText(dataSource);
        var databasePath = GetFullPath(dataSource, appRoot);

        EnsureNoTraversal(dataSource);
        EnsureDescendantPath(databasePath, allowedDirectory);
        EnsureNoReparsePointInPath(appRoot, allowedDirectory, "local bootstrap directory");
        EnsureNoReparsePointInPath(appRoot, Path.GetDirectoryName(databasePath)!, "local bootstrap database directory");
        EnsureDatabaseFileName(databasePath);
        EnsureSafeExistingDatabase(databasePath);

        return databasePath;
    }

    private static void ValidateDataSourceText(string dataSource)
    {
        var trimmed = dataSource.Trim();
        if (string.Equals(trimmed, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalBootstrapConfigurationException("Local bootstrap cannot use an in-memory SQLite database.");
        }

        if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalBootstrapConfigurationException("Local bootstrap does not accept SQLite file URI data sources.");
        }

        if (trimmed.StartsWith("\\\\", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            throw new LocalBootstrapConfigurationException("Local bootstrap database path cannot be a UNC or network share path.");
        }

        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            throw new LocalBootstrapConfigurationException("Local bootstrap database path cannot be a URI.");
        }
    }

    private static void EnsureNoTraversal(string dataSource)
    {
        var pathSegments = dataSource.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Any(segment => segment == ".."))
        {
            throw new LocalBootstrapConfigurationException("Local bootstrap database path cannot contain parent-directory traversal.");
        }
    }

    private static void EnsureDescendantPath(string databasePath, string allowedDirectory)
    {
        if (!IsDescendantPath(databasePath, allowedDirectory))
        {
            throw new LocalBootstrapConfigurationException(
                $"Local bootstrap database must be under {allowedDirectory}.");
        }
    }

    private static void EnsureDatabaseFileName(string databasePath)
    {
        var fileName = Path.GetFileName(databasePath);
        if (!fileName.StartsWith(DatabaseFileNamePrefix, StringComparison.OrdinalIgnoreCase) ||
            !AllowedDatabaseExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
        {
            throw new LocalBootstrapConfigurationException(
                $"Local bootstrap database file name must start with '{DatabaseFileNamePrefix}' and use .db, .sqlite, or .sqlite3.");
        }
    }

    private static void EnsureSafeExistingDatabase(string databasePath)
    {
        var fileInfo = new FileInfo(databasePath);
        if (!string.IsNullOrEmpty(fileInfo.LinkTarget))
        {
            throw new LocalBootstrapConfigurationException("Local bootstrap database file cannot be a symbolic link.");
        }

        if (!fileInfo.Exists)
        {
            return;
        }

        if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new LocalBootstrapConfigurationException("Local bootstrap database file cannot be a reparse point.");
        }

        EnsureDatabaseFileName(fileInfo.FullName);
    }

    private static void EnsureNoReparsePointInPath(string appRoot, string targetPath, string description)
    {
        var root = new DirectoryInfo(appRoot);
        for (var directory = new DirectoryInfo(targetPath); directory is not null; directory = directory.Parent)
        {
            if (directory.Exists)
            {
                if (!string.IsNullOrEmpty(directory.LinkTarget))
                {
                    throw new LocalBootstrapConfigurationException($"The {description} cannot include symbolic links.");
                }

                if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new LocalBootstrapConfigurationException($"The {description} cannot include reparse points.");
                }
            }

            if (PathsEqual(directory.FullName, root.FullName))
            {
                return;
            }
        }
    }

    private static bool ParseOptionalBoolean(IConfigurationSection section, string propertyName, bool defaultValue)
    {
        var key = section.Path + ConfigurationPath.KeyDelimiter + propertyName;
        var value = section.GetSection(propertyName).Value;

        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new LocalBootstrapConfigurationException($"{key} must be true or false.");
    }

    private static string GetFullPath(string path, string basePath)
    {
        return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(basePath, path));
    }

    private static bool IsDescendantPath(string candidatePath, string parentDirectory)
    {
        var parent = AppendDirectorySeparator(parentDirectory);
        return candidatePath.StartsWith(parent, PathComparison) && !PathsEqual(candidatePath, parentDirectory);
    }

    private static string AppendDirectorySeparator(string path)
    {
        return Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            PathComparison);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
