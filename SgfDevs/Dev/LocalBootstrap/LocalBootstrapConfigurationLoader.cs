#nullable enable

using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SgfDevs.Dev.LocalBootstrap;

public static class LocalBootstrapConfigurationLoader
{
    public const string ConfigPathEnvironmentVariable = "SGFDEVS_LOCAL_BOOTSTRAP_CONFIG_PATH";
    public const string ConfigFileName = "local-bootstrap.appsettings.json";

    public static bool AddLocalBootstrapConfiguration(ConfigurationManager configuration, IHostEnvironment environment)
    {
        var configuredPath = configuration[ConfigPathEnvironmentVariable];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return false;
        }

        if (!string.Equals(environment.EnvironmentName, Environments.Development, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var configPath = ValidateConfigPath(environment.ContentRootPath, configuredPath);
        configuration.AddJsonFile(configPath, optional: false, reloadOnChange: false);
        return true;
    }

    private static string ValidateConfigPath(string appRoot, string configuredPath)
    {
        var root = Path.GetFullPath(appRoot);
        var allowedDirectory = Path.GetFullPath(Path.Combine(root, "umbraco", "Data", "local-bootstrap"));
        var configPath = Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(root, configuredPath));

        if (!IsDescendantPath(configPath, allowedDirectory))
        {
            throw new LocalBootstrapConfigurationException(
                $"{ConfigPathEnvironmentVariable} must point under {allowedDirectory}.");
        }

        if (!string.Equals(Path.GetFileName(configPath), ConfigFileName, StringComparison.Ordinal))
        {
            throw new LocalBootstrapConfigurationException(
                $"{ConfigPathEnvironmentVariable} must point at {ConfigFileName}.");
        }

        EnsureNoSymlinkInPath(root, Path.GetDirectoryName(configPath)!, "local bootstrap config directory");

        if (!File.Exists(configPath))
        {
            throw new LocalBootstrapConfigurationException("The local bootstrap config file does not exist.");
        }

        var fileInfo = new FileInfo(configPath);
        if (!string.IsNullOrEmpty(fileInfo.LinkTarget))
        {
            throw new LocalBootstrapConfigurationException("The local bootstrap config file cannot be a symbolic link.");
        }

        return configPath;
    }

    private static void EnsureNoSymlinkInPath(string appRoot, string targetPath, string description)
    {
        var root = new DirectoryInfo(appRoot);
        for (var directory = new DirectoryInfo(targetPath); directory is not null; directory = directory.Parent)
        {
            if (directory.Exists && !string.IsNullOrEmpty(directory.LinkTarget))
            {
                throw new LocalBootstrapConfigurationException($"The {description} cannot include symbolic links.");
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
