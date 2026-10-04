using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace SgfDevs.Tests;

public sealed class LocalBootstrapScriptTests
{
    [Fact]
    public async Task Script_CreatesPrivateConfigAndRunsOnlySeaweedAndForegroundCms()
    {
        using var temp = TempScriptWorkspace.Create();

        var result = await temp.RunScriptAsync();

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Local bootstrap config:", result.Stdout);
        Assert.Contains("The password is not printed", result.Stdout);
        Assert.DoesNotContain("UnattendedUserPassword", result.Stdout);
        Assert.DoesNotContain("UnattendedUserPassword", result.Stderr);

        var configPath = temp.ConfigPath;
        Assert.True(File.Exists(configPath));
        Assert.False(File.Exists(temp.DatabasePath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(configPath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(temp.BootstrapDirectory));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(configPath));
        var root = document.RootElement;
        var cms = root.GetProperty("Umbraco").GetProperty("CMS");
        var password = cms.GetProperty("Unattended").GetProperty("UnattendedUserPassword").GetString();
        var hmacSecret = cms.GetProperty("Imaging").GetProperty("HMACSecretKey").GetString();
        Assert.False(string.IsNullOrWhiteSpace(password));
        Assert.False(string.IsNullOrWhiteSpace(hmacSecret));
        Assert.DoesNotContain(password!, result.Stdout);
        Assert.DoesNotContain(password!, result.Stderr);
        Assert.DoesNotContain(hmacSecret!, result.Stdout);
        Assert.DoesNotContain(hmacSecret!, result.Stderr);
        Assert.True(root.GetProperty("SGFDevs").GetProperty("LocalBootstrap").GetProperty("Enabled").GetBoolean());
        Assert.False(root.GetProperty("SGFDevs").GetProperty("LocalBootstrap").GetProperty("SeedFictionalContent").GetBoolean());
        Assert.Equal("Data Source=" + temp.DatabasePath + ";Cache=Shared", root.GetProperty("ConnectionStrings").GetProperty("umbracoDbDSN").GetString());
        Assert.Equal("Microsoft.Data.Sqlite", root.GetProperty("ConnectionStrings").GetProperty("umbracoDbDSN_ProviderName").GetString());
        Assert.Equal("http://127.0.0.1:8333", root.GetProperty("AWS").GetProperty("ServiceURL").GetString());
        Assert.True(root.GetProperty("AWS").GetProperty("ForcePathStyle").GetBoolean());
        Assert.Equal("sgf-dev-local", root.GetProperty("Umbraco").GetProperty("Storage").GetProperty("AWSS3").GetProperty("Media").GetProperty("BucketName").GetString());
        Assert.False(root.GetProperty("uSync").GetProperty("Settings").GetProperty("ImportOnFirstBoot").GetBoolean());
        Assert.Equal("None", root.GetProperty("uSync").GetProperty("Settings").GetProperty("ImportAtStartup").GetString());
        Assert.Equal("None", root.GetProperty("uSync").GetProperty("Settings").GetProperty("ExportAtStartup").GetString());
        Assert.Equal("None", root.GetProperty("uSync").GetProperty("Settings").GetProperty("ExportOnSave").GetString());

        Assert.Equal(new[] { "compose", "up", "-d", "seaweedfs" }, await File.ReadAllLinesAsync(temp.DockerArgsPath));
        var dotnetArgs = await File.ReadAllLinesAsync(temp.DotnetArgsPath);
        Assert.Contains("run", dotnetArgs);
        Assert.Contains("--no-launch-profile", dotnetArgs);
        Assert.Contains("--urls", dotnetArgs);
        Assert.Contains("http://127.0.0.1:5099", dotnetArgs);

        var childEnv = await File.ReadAllTextAsync(temp.DotnetEnvPath);
        Assert.Contains("ASPNETCORE_ENVIRONMENT=Development", childEnv);
        Assert.Contains("DOTNET_ENVIRONMENT=Development", childEnv);
        Assert.Contains("ASPNETCORE_URLS=http://127.0.0.1:5099", childEnv);
        Assert.Contains("CONFIG=" + temp.ConfigPath, childEnv);
        Assert.Contains("AWS_ACCESS_KEY_ID=sgf-dev-local", childEnv);
        Assert.Contains("AWS_SECRET_ACCESS_KEY=sgf-dev-local-password", childEnv);
        Assert.Contains("AWS_SESSION_TOKEN=", childEnv);
        Assert.Contains("AWS_PROFILE=", childEnv);
    }

    [Fact]
    public async Task Script_RefusesExplicitNonDevelopmentBeforeSideEffects()
    {
        using var temp = TempScriptWorkspace.Create();

        var result = await temp.RunScriptAsync(new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Production" });

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("local bootstrap only runs with Development", result.Stderr);
        Assert.False(File.Exists(temp.ConfigPath));
        Assert.False(File.Exists(temp.DockerArgsPath));
        Assert.False(File.Exists(temp.DotnetArgsPath));
    }

    [Fact]
    public async Task Script_RefusesExistingDatabaseWithoutOwnedConfig()
    {
        using var temp = TempScriptWorkspace.Create();
        Directory.CreateDirectory(temp.BootstrapDirectory);
        await File.WriteAllTextAsync(temp.DatabasePath, "not owned");

        var result = await temp.RunScriptAsync();

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("proves this script owns it", result.Stderr);
        Assert.False(File.Exists(temp.DockerArgsPath));
        Assert.False(File.Exists(temp.DotnetArgsPath));
    }

    [Fact]
    public async Task Script_ReusesOwnedConfigWithoutOverwritingSecrets()
    {
        using var temp = TempScriptWorkspace.Create();

        var first = await temp.RunScriptAsync();
        Assert.Equal(0, first.ExitCode);
        using var firstDocument = JsonDocument.Parse(await File.ReadAllTextAsync(temp.ConfigPath));
        var password = firstDocument.RootElement.GetProperty("Umbraco").GetProperty("CMS").GetProperty("Unattended").GetProperty("UnattendedUserPassword").GetString();

        var second = await temp.RunScriptAsync();

        Assert.Equal(0, second.ExitCode);
        using var secondDocument = JsonDocument.Parse(await File.ReadAllTextAsync(temp.ConfigPath));
        Assert.Equal(password, secondDocument.RootElement.GetProperty("Umbraco").GetProperty("CMS").GetProperty("Unattended").GetProperty("UnattendedUserPassword").GetString());
    }

    [Fact]
    public async Task Script_RefusesConfigThatCouldWriteMediaOffLoopback()
    {
        using var temp = TempScriptWorkspace.Create();
        Directory.CreateDirectory(temp.BootstrapDirectory);
        await File.WriteAllTextAsync(temp.ConfigPath, """
        {
          "SGFDevs": { "LocalBootstrap": { "Enabled": true, "SeedFictionalContent": false, "CreatedBy": "scripts/bootstrap-local-cms.py" } },
          "ConnectionStrings": { "umbracoDbDSN": "Data Source=DATABASE;Cache=Shared", "umbracoDbDSN_ProviderName": "Microsoft.Data.Sqlite" },
          "AWS": { "ServiceURL": "https://s3.amazonaws.com", "ForcePathStyle": false },
          "Umbraco": { "Storage": { "AWSS3": { "Media": { "BucketName": "prod" } } } }
        }
        """.Replace("DATABASE", temp.DatabasePath.Replace("\\", "\\\\")));
        File.SetUnixFileMode(temp.ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        var result = await temp.RunScriptAsync();

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("loopback SeaweedFS", result.Stderr);
        Assert.False(File.Exists(temp.DockerArgsPath));
        Assert.False(File.Exists(temp.DotnetArgsPath));
    }

    private sealed class TempScriptWorkspace : IDisposable
    {
        private TempScriptWorkspace(string path)
        {
            Root = path;
            RepoRoot = Path.Combine(path, "repo");
            FakeBin = Path.Combine(path, "bin");
            DockerArgsPath = Path.Combine(path, "docker.args");
            DotnetArgsPath = Path.Combine(path, "dotnet.args");
            DotnetEnvPath = Path.Combine(path, "dotnet.env");
            BootstrapDirectory = Path.Combine(RepoRoot, "SgfDevs", "umbraco", "Data", "local-bootstrap");
            ConfigPath = Path.Combine(BootstrapDirectory, "local-bootstrap.appsettings.json");
            DatabasePath = Path.Combine(BootstrapDirectory, "local-bootstrap.sqlite");
        }

        public string Root { get; }
        public string RepoRoot { get; }
        public string FakeBin { get; }
        public string DockerArgsPath { get; }
        public string DotnetArgsPath { get; }
        public string DotnetEnvPath { get; }
        public string BootstrapDirectory { get; }
        public string ConfigPath { get; }
        public string DatabasePath { get; }

        public static TempScriptWorkspace Create()
        {
            var path = Path.Combine(Path.GetTempPath(), "sgf-local-bootstrap-script-tests", Guid.NewGuid().ToString("N"));
            var workspace = new TempScriptWorkspace(path);
            Directory.CreateDirectory(Path.Combine(workspace.RepoRoot, "SgfDevs", "Dev", "LocalBootstrap"));
            Directory.CreateDirectory(workspace.FakeBin);
            File.WriteAllText(Path.Combine(workspace.RepoRoot, "SgfDevs", "SgfDevs.csproj"), "<Project />");
            File.WriteAllText(Path.Combine(workspace.RepoRoot, "SgfDevs", "Program.cs"), "// test repo");
            File.WriteAllText(Path.Combine(workspace.RepoRoot, "SgfDevs", "Dev", "LocalBootstrap", "LocalBootstrapGuard.cs"), "// test repo");
            File.WriteAllText(Path.Combine(workspace.RepoRoot, "compose.yaml"), "services:\n  seaweedfs:\n    image: test\n");
            workspace.WriteFakeExecutables();
            return workspace;
        }

        public async Task<ScriptRunResult> RunScriptAsync(Dictionary<string, string>? extraEnvironment = null)
        {
            if (OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("The bootstrap script test uses POSIX fake executables.");
            }

            var startInfo = new ProcessStartInfo("python3")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = SourceRepoRoot,
            };
            startInfo.ArgumentList.Add(Path.Combine(SourceRepoRoot, "scripts", "bootstrap-local-cms.py"));
            startInfo.ArgumentList.Add("--repo-root");
            startInfo.ArgumentList.Add(RepoRoot);
            startInfo.Environment["PATH"] = FakeBin + Path.PathSeparator + (startInfo.Environment["PATH"] ?? string.Empty);
            startInfo.Environment["FAKE_DOCKER_ARGS"] = DockerArgsPath;
            startInfo.Environment["FAKE_DOTNET_ARGS"] = DotnetArgsPath;
            startInfo.Environment["FAKE_DOTNET_ENV"] = DotnetEnvPath;
            startInfo.Environment.Remove("ASPNETCORE_ENVIRONMENT");
            startInfo.Environment.Remove("DOTNET_ENVIRONMENT");
            if (extraEnvironment is not null)
            {
                foreach (var pair in extraEnvironment)
                {
                    startInfo.Environment[pair.Key] = pair.Value;
                }
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start bootstrap script.");
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ScriptRunResult(process.ExitCode, stdout, stderr);
        }

        private void WriteFakeExecutables()
        {
            File.WriteAllText(Path.Combine(FakeBin, "docker"), """
            #!/usr/bin/env bash
            printf '%s\n' "$@" > "$FAKE_DOCKER_ARGS"
            exit 0
            """);
            File.WriteAllText(Path.Combine(FakeBin, "dotnet"), """
            #!/usr/bin/env bash
            printf '%s\n' "$@" > "$FAKE_DOTNET_ARGS"
            {
              printf 'ASPNETCORE_ENVIRONMENT=%s\n' "$ASPNETCORE_ENVIRONMENT"
              printf 'DOTNET_ENVIRONMENT=%s\n' "$DOTNET_ENVIRONMENT"
              printf 'ASPNETCORE_URLS=%s\n' "$ASPNETCORE_URLS"
              printf 'CONFIG=%s\n' "$SGFDEVS_LOCAL_BOOTSTRAP_CONFIG_PATH"
              printf 'AWS_ACCESS_KEY_ID=%s\n' "$AWS_ACCESS_KEY_ID"
              printf 'AWS_SECRET_ACCESS_KEY=%s\n' "$AWS_SECRET_ACCESS_KEY"
              printf 'AWS_SESSION_TOKEN=%s\n' "$AWS_SESSION_TOKEN"
              printf 'AWS_PROFILE=%s\n' "$AWS_PROFILE"
            } > "$FAKE_DOTNET_ENV"
            exit 0
            """);
            File.SetUnixFileMode(Path.Combine(FakeBin, "docker"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(Path.Combine(FakeBin, "dotnet"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string SourceRepoRoot
        {
            get
            {
                for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                {
                    if (File.Exists(Path.Combine(directory.FullName, "scripts", "bootstrap-local-cms.py")) &&
                        File.Exists(Path.Combine(directory.FullName, "SgfDevs", "SgfDevs.csproj")))
                    {
                        return directory.FullName;
                    }
                }

                throw new DirectoryNotFoundException("Cannot locate the cms.sgf.dev source repo.");
            }
        }
    }

    private sealed record ScriptRunResult(int ExitCode, string Stdout, string Stderr);
}
