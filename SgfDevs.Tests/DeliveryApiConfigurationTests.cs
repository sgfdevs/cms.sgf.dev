using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Extensions;
using Xunit;

namespace SgfDevs.Tests;

public class DeliveryApiConfigurationTests
{
    private static readonly string[] PublicTypes =
        ["home", "events", "event", "companies", "groups", "jobs"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicApiConfiguration_DoesNotEnablePreviewMediaOrMembers(bool development)
    {
        var configuration = LoadConfiguration(development);
        var settings = configuration.GetSection("Umbraco:CMS:DeliveryApi").Get<DeliveryApiSettings>()!;

        Assert.True(settings.Enabled);
        Assert.True(settings.PublicAccess);
        Assert.True(string.IsNullOrEmpty(settings.ApiKey));
        Assert.False(settings.Media.Enabled);
        Assert.False(settings.Media.PublicAccess);
        Assert.False(settings.MemberAuthorizationIsEnabled());
        Assert.False(settings.OpenApi.GenerateContentTypeSchemas);
        Assert.Equal(PublicTypes.Order(), settings.AllowedContentTypeAliases.Order());
        Assert.All(PublicTypes, alias => Assert.True(settings.IsAllowedContentType(alias)));

        foreach (var path in Directory.GetFiles(Path.Combine(ProjectDirectory, "uSync/v18/ContentTypes"), "*.config"))
        {
            var alias = XDocument.Load(path).Root!.Attribute("Alias")!.Value;
            Assert.Equal(PublicTypes.Contains(alias), settings.IsAllowedContentType(alias));
        }

        Assert.False(settings.IsAllowedContentType("futurePrivateType"));
    }

    [Fact]
    public void AllowedSchemas_ContainOnlyReviewedPropertiesAndNoCompositions()
    {
        foreach (var alias in PublicTypes)
        {
            var root = XDocument.Load(Path.Combine(ProjectDirectory, $"uSync/v18/ContentTypes/{alias}.config")).Root!;
            Assert.Equal("false", root.Element("Info")!.Element("IsElement")!.Value);
            Assert.Empty(root.Descendants("Composition"));

            var properties = root.Descendants("GenericProperty").ToArray();
            if (alias == "event")
            {
                Assert.Equal(["date", "helpTextPresentations"], properties.Select(p => p.Element("Alias")!.Value).Order());
                Assert.Equal(["Umbraco.DateTime", "Umbraco.Label"], properties.Select(p => p.Element("Type")!.Value).Order());
            }
            else
            {
                Assert.Empty(properties);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublisherIncoming_IsDisabledWithNoTrackedCredentials(bool development)
    {
        var settings = LoadConfiguration(development).GetSection("uSync:Publisher:Settings");
        Assert.False(settings.GetValue<bool>("IncomingEnabled"));
        Assert.True(string.IsNullOrEmpty(settings["AppId"]));
        Assert.True(string.IsNullOrEmpty(settings["AppKey"]));

        foreach (var path in Directory.GetFiles(ProjectDirectory, "appsettings*.json*")
                     .Where(path => !Path.GetFileName(path).StartsWith("appsettings-schema")))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("uSync", out var usync) ||
                !usync.TryGetProperty("Publisher", out var publisher) ||
                !publisher.TryGetProperty("Settings", out var publisherSettings))
            {
                continue;
            }

            foreach (var credential in new[] { "AppId", "AppKey" })
            {
                Assert.True(!publisherSettings.TryGetProperty(credential, out var value) ||
                            string.IsNullOrEmpty(value.GetString()), $"Tracked {credential} in {Path.GetFileName(path)}");
            }
        }
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
