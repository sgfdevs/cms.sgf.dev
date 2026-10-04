using System.Text.Json;
using Microsoft.Extensions.Configuration;
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
    public void Program_ValidatesLocalBootstrapBeforeUmbracoCanBuildOrBoot()
    {
        var program = File.ReadAllText(Path.Combine(ProjectDirectory, "Program.cs"));

        var guardIndex = program.IndexOf("LocalBootstrapGuard.ValidateStartupConfiguration", StringComparison.Ordinal);
        var createBuilderIndex = program.IndexOf("builder.CreateUmbracoBuilder()", StringComparison.Ordinal);
        var buildIndex = program.IndexOf("umbracoBuilder.Build()", StringComparison.Ordinal);
        var bootIndex = program.IndexOf("app.BootUmbracoAsync()", StringComparison.Ordinal);

        Assert.True(guardIndex >= 0, "Program.cs must call the local bootstrap guard.");
        Assert.True(guardIndex < createBuilderIndex, "The local bootstrap guard must run before CreateUmbracoBuilder.");
        Assert.True(guardIndex < buildIndex, "The local bootstrap guard must run before UmbracoBuilder.Build.");
        Assert.True(guardIndex < bootIndex, "The local bootstrap guard must run before BootUmbracoAsync.");
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
