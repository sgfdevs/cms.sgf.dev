#nullable enable

using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SgfDevs.Dev.LocalBootstrap;

public enum LocalSchemaBootstrapSupportCap
{
    None
}

public enum LocalSchemaBootstrapPhase
{
    Firstboot,
    Reuse
}

public sealed record LocalSchemaBootstrapPolicy(
    int PolicyVersion,
    string Phase,
    bool Enabled,
    string LogicalDbId,
    bool SchemaSourceOptIn,
    bool SeedFictionalContent);

public sealed record LocalSchemaProfileName(LocalSchemaBootstrapPhase Phase, string SafeId, string FileName);

public static class LocalSchemaBootstrapSupport
{
    public const LocalSchemaBootstrapSupportCap Cap = LocalSchemaBootstrapSupportCap.None;
}

public static class LocalSchemaBootstrapGuard
{
    public const int PolicyVersion = 2;
    public const int MinimumSafeIdLength = 12;
    public const string PhaseFirstboot = "schema-firstboot";
    public const string PhaseReuse = "schema-reuse";
    public const string ManagedFilePrefix = "local-bootstrap-schema-v2-";
    public const string FirstbootProfileSuffix = ".firstboot.appsettings.json";
    public const string ReuseProfileSuffix = ".reuse.appsettings.json";
    public const string DatabaseSuffix = ".sqlite";
    public const string UnsupportedMessage = "Schema phase not supported by this build.";

    public static void RejectUnsupportedConfigPath(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!IsDevelopment(environment.EnvironmentName))
        {
            return;
        }

        var configuredPath = configuration[LocalBootstrapConfigurationLoader.ConfigPathEnvironmentVariable];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return;
        }

        var fileName = Path.GetFileName(configuredPath.Trim());
        if (IsManagedProfileFileName(fileName))
        {
            ThrowUnsupported();
        }
    }

    public static void RejectUnsupportedEffectiveRequest(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!IsDevelopment(environment.EnvironmentName))
        {
            return;
        }

        var phase = configuration[$"{LocalBootstrapOptions.SectionName}:Phase"]?.Trim();
        if (IsSchemaPhase(phase))
        {
            ThrowUnsupported();
        }

        var policyVersion = configuration[$"{LocalBootstrapOptions.SectionName}:PolicyVersion"]?.Trim();
        if (string.Equals(policyVersion, PolicyVersion.ToString(), StringComparison.Ordinal))
        {
            ThrowUnsupported();
        }
    }

    public static bool IsManagedProfileFileName(string? fileName) =>
        TryParseProfileFileName(fileName, out _);

    public static LocalSchemaProfileName ValidateProfileFileName(string fileName)
    {
        if (TryParseProfileFileName(fileName, out var profile))
        {
            return profile;
        }

        throw new LocalBootstrapConfigurationException("Schema bootstrap profile name is not a managed v2 basename.");
    }

    public static string FirstbootProfileFileName(string safeId)
    {
        ValidateSafeId(safeId);
        return ManagedFilePrefix + safeId + FirstbootProfileSuffix;
    }

    public static string ReuseProfileFileName(string safeId)
    {
        ValidateSafeId(safeId);
        return ManagedFilePrefix + safeId + ReuseProfileSuffix;
    }

    public static string DatabaseFileName(string safeId)
    {
        ValidateSafeId(safeId);
        return ManagedFilePrefix + safeId + DatabaseSuffix;
    }

    public static string ValidateManagedDatabasePath(string appRoot, string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap database path is required.");
        }

        ValidateDataSourceText(configuredPath);
        EnsureNoTraversal(configuredPath);

        var root = GetFullPath(appRoot, Directory.GetCurrentDirectory());
        var allowedDirectory = GetSchemaDirectory(root);
        var databasePath = GetFullPath(configuredPath, root);
        EnsureDescendantPath(databasePath, allowedDirectory);
        EnsureNoReparsePointInPath(root, allowedDirectory, "schema bootstrap directory");
        EnsureNoReparsePointInPath(root, Path.GetDirectoryName(databasePath)!, "schema bootstrap database directory");

        var fileName = Path.GetFileName(databasePath);
        if (!fileName.StartsWith(ManagedFilePrefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(DatabaseSuffix, StringComparison.Ordinal))
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap database name is not a managed v2 basename.");
        }

        var safeId = fileName[ManagedFilePrefix.Length..^DatabaseSuffix.Length];
        ValidateSafeId(safeId);
        return databasePath;
    }

    public static void ValidateProviderAndConnectionString(string? providerName, string? connectionString, string appRoot)
    {
        if (!string.Equals(providerName?.Trim(), LocalBootstrapGuard.RequiredProviderName, StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalBootstrapConfigurationException(
                $"Schema bootstrap requires ConnectionStrings:umbracoDbDSN_ProviderName to be {LocalBootstrapGuard.RequiredProviderName}.");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap requires ConnectionStrings:umbracoDbDSN.");
        }

        SqliteConnectionStringBuilder builder;
        try
        {
            builder = new SqliteConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap SQLite connection string is invalid.");
        }

        if (builder.Mode is SqliteOpenMode.Memory)
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap cannot use SQLite memory mode.");
        }

        ValidateManagedDatabasePath(appRoot, builder.DataSource);
    }

    public static void ValidateFirstbootFamilyAbsent(string databasePath)
    {
        foreach (var path in EnumerateManagedDatabaseFamily(databasePath))
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                throw new LocalBootstrapConfigurationException("Schema firstboot requires a fresh database family.");
            }
        }
    }

    public static void ValidateReuseFamilyMetadata(string databasePath)
    {
        EnsureRegularFile(databasePath, "Schema reuse requires an existing regular database file.");

        foreach (var path in EnumerateManagedDatabaseFamily(databasePath).Where(path => !PathsEqual(path, databasePath)))
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                continue;
            }

            EnsureKnownDatabaseFamilyMember(databasePath, path);
            EnsureRegularFile(path, "Schema reuse sidecars must be regular files.");
        }
    }

    public static void ValidateSafeId(string safeId)
    {
        if (safeId.Length < MinimumSafeIdLength || safeId.Any(static c => !IsSafeIdCharacter(c)))
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap safe id must be at least 12 lowercase letters or digits.");
        }
    }

    private static bool IsSafeIdCharacter(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool TryParseProfileFileName(string? fileName, out LocalSchemaProfileName profile)
    {
        profile = default!;
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.StartsWith(ManagedFilePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (TryParseProfileFileName(fileName, FirstbootProfileSuffix, LocalSchemaBootstrapPhase.Firstboot, out profile) ||
            TryParseProfileFileName(fileName, ReuseProfileSuffix, LocalSchemaBootstrapPhase.Reuse, out profile))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseProfileFileName(
        string fileName,
        string suffix,
        LocalSchemaBootstrapPhase phase,
        out LocalSchemaProfileName profile)
    {
        profile = default!;
        if (!fileName.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var safeId = fileName[ManagedFilePrefix.Length..^suffix.Length];
        try
        {
            ValidateSafeId(safeId);
        }
        catch (LocalBootstrapConfigurationException)
        {
            return false;
        }

        profile = new LocalSchemaProfileName(phase, safeId, fileName);
        return true;
    }

    private static void ValidateDataSourceText(string dataSource)
    {
        var trimmed = dataSource.Trim();
        if (string.Equals(trimmed, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap cannot use an in-memory SQLite database.");
        }

        if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap does not accept SQLite file URI data sources.");
        }

        if (trimmed.StartsWith("\\\\", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap database path cannot be a UNC or network share path.");
        }

        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap database path cannot be a URI.");
        }
    }

    private static void EnsureNoTraversal(string path)
    {
        var pathSegments = path.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Any(segment => segment == ".."))
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap paths cannot contain parent-directory traversal.");
        }
    }

    private static string[] EnumerateManagedDatabaseFamily(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        var fileName = Path.GetFileName(databasePath);
        if (directory is null || !Directory.Exists(directory))
        {
            return [databasePath];
        }

        return Directory.EnumerateFileSystemEntries(directory, fileName + "*")
            .Where(path => PathsEqual(path, databasePath) || Path.GetFileName(path).StartsWith(fileName + "-", StringComparison.Ordinal))
            .ToArray();
    }

    private static void EnsureKnownDatabaseFamilyMember(string databasePath, string candidatePath)
    {
        var fileName = Path.GetFileName(databasePath);
        var candidateName = Path.GetFileName(candidatePath);
        if (!PathsEqual(candidatePath, databasePath) &&
            !string.Equals(candidateName, fileName + "-wal", StringComparison.Ordinal) &&
            !string.Equals(candidateName, fileName + "-shm", StringComparison.Ordinal) &&
            !string.Equals(candidateName, fileName + "-journal", StringComparison.Ordinal) &&
            !candidateName.StartsWith(fileName + "-mj", StringComparison.Ordinal))
        {
            throw new LocalBootstrapConfigurationException("Schema bootstrap database family contains an unknown sidecar.");
        }
    }

    private static void EnsureRegularFile(string path, string message)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists || !string.IsNullOrEmpty(fileInfo.LinkTarget) ||
            fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
            fileInfo.Attributes.HasFlag(FileAttributes.Directory))
        {
            throw new LocalBootstrapConfigurationException(message);
        }
    }

    private static bool IsSchemaPhase(string? phase) =>
        string.Equals(phase, PhaseFirstboot, StringComparison.Ordinal) ||
        string.Equals(phase, PhaseReuse, StringComparison.Ordinal);

    private static bool IsDevelopment(string environmentName) =>
        string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase);

    private static void ThrowUnsupported() =>
        throw new LocalBootstrapConfigurationException(UnsupportedMessage);

    private static string GetSchemaDirectory(string appRoot) =>
        GetFullPath(Path.Combine(appRoot, "umbraco", "Data", "local-bootstrap"), appRoot);

    private static string GetFullPath(string path, string basePath) =>
        Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(basePath, path));

    private static void EnsureDescendantPath(string candidatePath, string parentDirectory)
    {
        if (!IsDescendantPath(candidatePath, parentDirectory))
        {
            throw new LocalBootstrapConfigurationException($"Schema bootstrap path must be under {parentDirectory}.");
        }
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

    private static bool IsDescendantPath(string candidatePath, string parentDirectory)
    {
        var parent = Path.EndsInDirectorySeparator(parentDirectory) ? parentDirectory : parentDirectory + Path.DirectorySeparatorChar;
        return candidatePath.StartsWith(parent, PathComparison) && !PathsEqual(candidatePath, parentDirectory);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            PathComparison);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
