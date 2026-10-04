using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SgfDevs.Tests;

[SupportedOSPlatform("linux")]
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
        Assert.Equal(1, root.GetProperty("SGFDevs").GetProperty("LocalBootstrap").GetProperty("PolicyVersion").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("SGFDevs").GetProperty("LocalBootstrap").GetProperty("PolicySignature").GetString()));
        Assert.Equal("Data Source=" + temp.DatabasePath + ";Cache=Shared", root.GetProperty("ConnectionStrings").GetProperty("umbracoDbDSN").GetString());
        Assert.Equal("Microsoft.Data.Sqlite", root.GetProperty("ConnectionStrings").GetProperty("umbracoDbDSN_ProviderName").GetString());
        Assert.Equal("http://127.0.0.1:8333", root.GetProperty("AWS").GetProperty("ServiceURL").GetString());
        Assert.True(root.GetProperty("AWS").GetProperty("ForcePathStyle").GetBoolean());
        Assert.Equal("sgf-dev-local", root.GetProperty("Umbraco").GetProperty("Storage").GetProperty("AWSS3").GetProperty("Media").GetProperty("BucketName").GetString());
        Assert.False(root.GetProperty("uSync").GetProperty("Settings").GetProperty("ImportOnFirstBoot").GetBoolean());
        Assert.Equal("None", root.GetProperty("uSync").GetProperty("Settings").GetProperty("ImportAtStartup").GetString());
        Assert.Equal("None", root.GetProperty("uSync").GetProperty("Settings").GetProperty("ExportAtStartup").GetString());
        Assert.Equal("None", root.GetProperty("uSync").GetProperty("Settings").GetProperty("ExportOnSave").GetString());

        Assert.Equal(new[]
        {
            "--context",
            "default",
            "compose",
            "-f",
            Path.Combine(temp.RepoRoot, "compose.yaml"),
            "--project-name",
            "sgf-dev-local",
            "up",
            "-d",
            "seaweedfs"
        }, await File.ReadAllLinesAsync(temp.DockerArgsPath));
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
        Assert.Contains("AWS_DEFAULT_PROFILE=", childEnv);
        Assert.Contains("AWS_EC2_METADATA_DISABLED=true", childEnv);
        Assert.Contains("NO_PROXY=127.0.0.1,localhost,::1", childEnv);
        Assert.Contains("SENTRY_DSN=", childEnv);
        Assert.Contains("Sentry__Dsn=", childEnv);
        Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT=", childEnv);
        Assert.Contains("OTEL_EXPORTER_OTLP_TRACES_HEADERS=", childEnv);
        Assert.Contains("DOTNET_STARTUP_HOOKS=", childEnv);
        Assert.Contains("CORECLR_ENABLE_PROFILING=", childEnv);
        Assert.Contains("HTTP_PROXY=", childEnv);
        Assert.Contains("HTTPS_PROXY=", childEnv);
        Assert.Contains("ALL_PROXY=", childEnv);

        var dockerEnv = await File.ReadAllTextAsync(temp.DockerEnvPath);
        Assert.Contains("COMPOSE_FILE=", dockerEnv);
        Assert.Contains("COMPOSE_PROJECT_NAME=", dockerEnv);
        Assert.Contains("DOCKER_HOST=", dockerEnv);
        Assert.Contains("DOCKER_CONTEXT=", dockerEnv);
        Assert.Contains("HTTP_PROXY=", dockerEnv);
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
        File.SetUnixFileMode(temp.BootstrapDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var originalMode = File.GetUnixFileMode(temp.BootstrapDirectory);
        await File.WriteAllTextAsync(temp.DatabasePath, "not owned");

        var result = await temp.RunScriptAsync();

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("proves this script owns it", result.Stderr);
        Assert.Equal(originalMode, File.GetUnixFileMode(temp.BootstrapDirectory));
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

    [Theory]
    [InlineData("uSync.Settings.ImportOnFirstBoot", true)]
    [InlineData("uSync.Settings.FirstBootGroup", "All")]
    [InlineData("uSync.Settings.ImportAtStartup", "All")]
    [InlineData("SGFDevs.NewsletterEndpoint", "https://example.invalid/newsletter")]
    [InlineData("SGFDevs.Sessionize.BaseUrl", "https://sessionize.com")]
    [InlineData("AWS.Profile", "prod")]
    [InlineData("Umbraco.CMS.Unattended.UnattendedUserPassword", "changed-password")]
    public async Task Script_RefusesTamperedOwnedConfig(string dottedPath, object value)
    {
        using var temp = TempScriptWorkspace.Create();
        var first = await temp.RunScriptAsync();
        Assert.Equal(0, first.ExitCode);
        File.Delete(temp.DockerArgsPath);
        File.Delete(temp.DotnetArgsPath);

        var root = JsonNode.Parse(await File.ReadAllTextAsync(temp.ConfigPath))!.AsObject();
        SetJsonValue(root, dottedPath.Split('.'), JsonValue.Create(value)!);
        await File.WriteAllTextAsync(temp.ConfigPath, root.ToJsonString() + "\n");
        File.SetUnixFileMode(temp.ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.SetUnixFileMode(temp.BootstrapDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        var originalDirectoryMode = File.GetUnixFileMode(temp.BootstrapDirectory);

        var result = await temp.RunScriptAsync();

        Assert.Equal(2, result.ExitCode);
        Assert.Matches("policy signature|generated local admin password", result.Stderr);
        Assert.Equal(originalDirectoryMode, File.GetUnixFileMode(temp.BootstrapDirectory));
        Assert.False(File.Exists(temp.DockerArgsPath));
        Assert.False(File.Exists(temp.DotnetArgsPath));
    }

    [Fact]
    public async Task Script_ClearsInheritedTelemetryProxyAndInstrumentationEnvironmentForCms()
    {
        using var temp = TempScriptWorkspace.Create();

        var result = await temp.RunScriptAsync(new Dictionary<string, string>
        {
            ["SENTRY_DSN"] = "https://public@example.invalid/1",
            ["Sentry__Dsn"] = "https://public@example.invalid/2",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "https://otel.example.invalid",
            ["OTEL_EXPORTER_OTLP_TRACES_HEADERS"] = "api-key=secret",
            ["DOTNET_STARTUP_HOOKS"] = "/tmp/hook.dll",
            ["CORECLR_ENABLE_PROFILING"] = "1",
            ["HTTP_PROXY"] = "http://proxy.example.invalid",
            ["HTTPS_PROXY"] = "http://proxy.example.invalid",
            ["ALL_PROXY"] = "socks5://proxy.example.invalid",
            ["COMPOSE_FILE"] = Path.Combine(temp.Root, "evil-compose.yaml"),
            ["COMPOSE_PROJECT_NAME"] = "evil-project",
        });

        Assert.Equal(0, result.ExitCode);
        var childEnv = await File.ReadAllTextAsync(temp.DotnetEnvPath);
        Assert.Contains("SENTRY_DSN=", childEnv);
        Assert.Contains("Sentry__Dsn=", childEnv);
        Assert.Contains("OTEL_EXPORTER_OTLP_ENDPOINT=", childEnv);
        Assert.Contains("OTEL_EXPORTER_OTLP_TRACES_HEADERS=", childEnv);
        Assert.Contains("DOTNET_STARTUP_HOOKS=", childEnv);
        Assert.Contains("CORECLR_ENABLE_PROFILING=", childEnv);
        Assert.Contains("HTTP_PROXY=", childEnv);
        Assert.Contains("HTTPS_PROXY=", childEnv);
        Assert.Contains("ALL_PROXY=", childEnv);
        var dockerEnv = await File.ReadAllTextAsync(temp.DockerEnvPath);
        Assert.Contains("COMPOSE_FILE=", dockerEnv);
        Assert.Contains("COMPOSE_PROJECT_NAME=", dockerEnv);
    }

    [Theory]
    [InlineData("DOCKER_HOST", "tcp://remote.example.invalid:2376")]
    [InlineData("DOCKER_CONTEXT", "remote-prod")]
    public async Task Script_RefusesInjectedRemoteDockerSelectionBeforeCreatingPrivateConfig(string key, string value)
    {
        using var temp = TempScriptWorkspace.Create();

        var result = await temp.RunScriptAsync(new Dictionary<string, string>
        {
            [key] = value,
            ["COMPOSE_FILE"] = Path.Combine(temp.Root, "evil-compose.yaml"),
        });

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("local Docker context", result.Stderr);
        Assert.False(File.Exists(temp.ConfigPath));
        Assert.False(File.Exists(temp.DockerArgsPath));
        Assert.False(File.Exists(temp.DotnetArgsPath));
    }

    [Fact]
    public async Task Script_RefusesRemoteDefaultDockerContextBeforeCreatingPrivateConfig()
    {
        using var temp = TempScriptWorkspace.Create();

        var result = await temp.RunScriptAsync(new Dictionary<string, string> { ["FAKE_DOCKER_CONTEXT_ENDPOINT"] = "tcp://remote.example.invalid:2376" });

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("not a local endpoint", result.Stderr);
        Assert.False(File.Exists(temp.ConfigPath));
        Assert.False(File.Exists(temp.DockerArgsPath));
        Assert.False(File.Exists(temp.DotnetArgsPath));
    }

    [Theory]
    [InlineData("docker")]
    [InlineData("dotnet")]
    public async Task Script_ValidatesExecutablesBeforeCreatingPrivateConfig(string executableName)
    {
        using var temp = TempScriptWorkspace.Create();
        File.Delete(Path.Combine(temp.FakeBin, executableName));

        var result = await temp.RunScriptAsync(includeSystemPath: false);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains($"required executable not found on PATH: {executableName}", result.Stderr);
        Assert.False(File.Exists(temp.ConfigPath));
        Assert.False(Directory.Exists(temp.BootstrapDirectory));
        Assert.False(File.Exists(temp.DockerArgsPath));
        Assert.False(File.Exists(temp.DotnetArgsPath));
    }

    private static void SetJsonValue(JsonObject root, IReadOnlyList<string> path, JsonNode value)
    {
        JsonObject current = root;
        for (var index = 0; index < path.Count - 1; index++)
        {
            var key = path[index];
            if (current[key] is not JsonObject next)
            {
                next = new JsonObject();
                current[key] = next;
            }

            current = next;
        }

        current[path[^1]] = value;
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
            DockerEnvPath = Path.Combine(path, "docker.env");
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
        public string DockerEnvPath { get; }
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

        public async Task<ScriptRunResult> RunScriptAsync(
            Dictionary<string, string>? extraEnvironment = null,
            bool includeSystemPath = true)
        {
            if (OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("The bootstrap script test uses POSIX fake executables.");
            }

            var startInfo = new ProcessStartInfo(FindPython())
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = SourceRepoRoot,
            };
            startInfo.ArgumentList.Add(Path.Combine(SourceRepoRoot, "scripts", "bootstrap-local-cms.py"));
            startInfo.ArgumentList.Add("--repo-root");
            startInfo.ArgumentList.Add(RepoRoot);
            startInfo.Environment["PATH"] = includeSystemPath
                ? FakeBin + Path.PathSeparator + (startInfo.Environment["PATH"] ?? string.Empty)
                : FakeBin;
            startInfo.Environment["FAKE_DOCKER_ARGS"] = DockerArgsPath;
            startInfo.Environment["FAKE_DOTNET_ARGS"] = DotnetArgsPath;
            startInfo.Environment["FAKE_DOTNET_ENV"] = DotnetEnvPath;
            startInfo.Environment["FAKE_DOCKER_ENV"] = DockerEnvPath;
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

        private static string FindPython()
        {
            foreach (var candidate in new[] { "/usr/bin/python3", "/usr/local/bin/python3" })
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
            foreach (var directory in pathDirectories)
            {
                var candidate = Path.Combine(directory, "python3");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "python3";
        }

        private void WriteFakeExecutables()
        {
            File.WriteAllText(Path.Combine(FakeBin, "docker"), """
            #!/bin/sh
            if [ "$1" = "context" ] && [ "$2" = "inspect" ]; then
              printf '"%s"\n' "${FAKE_DOCKER_CONTEXT_ENDPOINT:-unix:///var/run/docker.sock}"
              exit 0
            fi
            printf '%s\n' "$@" > "$FAKE_DOCKER_ARGS"
            {
              printf 'COMPOSE_FILE=%s\n' "$COMPOSE_FILE"
              printf 'COMPOSE_PROJECT_NAME=%s\n' "$COMPOSE_PROJECT_NAME"
              printf 'DOCKER_HOST=%s\n' "$DOCKER_HOST"
              printf 'DOCKER_CONTEXT=%s\n' "$DOCKER_CONTEXT"
              printf 'HTTP_PROXY=%s\n' "$HTTP_PROXY"
            } > "$FAKE_DOCKER_ENV"
            exit 0
            """);
            File.WriteAllText(Path.Combine(FakeBin, "dotnet"), """
            #!/bin/sh
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
              printf 'AWS_DEFAULT_PROFILE=%s\n' "$AWS_DEFAULT_PROFILE"
              printf 'AWS_EC2_METADATA_DISABLED=%s\n' "$AWS_EC2_METADATA_DISABLED"
              printf 'NO_PROXY=%s\n' "$NO_PROXY"
              printf 'SENTRY_DSN=%s\n' "$SENTRY_DSN"
              printf 'Sentry__Dsn=%s\n' "$Sentry__Dsn"
              printf 'OTEL_EXPORTER_OTLP_ENDPOINT=%s\n' "$OTEL_EXPORTER_OTLP_ENDPOINT"
              printf 'OTEL_EXPORTER_OTLP_TRACES_HEADERS=%s\n' "$OTEL_EXPORTER_OTLP_TRACES_HEADERS"
              printf 'DOTNET_STARTUP_HOOKS=%s\n' "$DOTNET_STARTUP_HOOKS"
              printf 'CORECLR_ENABLE_PROFILING=%s\n' "$CORECLR_ENABLE_PROFILING"
              printf 'HTTP_PROXY=%s\n' "$HTTP_PROXY"
              printf 'HTTPS_PROXY=%s\n' "$HTTPS_PROXY"
              printf 'ALL_PROXY=%s\n' "$ALL_PROXY"
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
