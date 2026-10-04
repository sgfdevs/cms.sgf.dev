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
        ["home", "events", "event", "companies", "company", "groups", "jobs", "page"];

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
        foreach (var alias in new[] { "group", "job", "leadership", "member", "markdown", "richTextEditor", "about" })
        {
            Assert.False(settings.IsAllowedContentType(alias));
        }
        Assert.False(settings.RichTextOutputAsJson);
    }

    [Fact]
    public void AllowedSchemas_ContainOnlyReviewedPropertiesAndPageMeta()
    {
        foreach (var alias in PublicTypes)
        {
            var root = XDocument.Load(Path.Combine(ProjectDirectory, $"uSync/v18/ContentTypes/{alias}.config")).Root!;
            Assert.Equal("false", root.Element("Info")!.Element("IsElement")!.Value);
            var properties = root.Descendants("GenericProperty").ToArray();
            if (alias == "page")
            {
                Assert.Equal(["meta"], root.Descendants("Composition").Select(p => p.Value));
                Assert.Equal(["blocks"], properties.Select(p => p.Element("Alias")!.Value));
                Assert.Equal(["Umbraco.BlockList"], properties.Select(p => p.Element("Type")!.Value));
                var meta = XDocument.Load(Path.Combine(ProjectDirectory, "uSync/v18/ContentTypes/meta.config")).Root!;
                Assert.Empty(meta.Descendants("Composition"));
                Assert.Equal(["OgImage", "description", "titleTag"],
                    meta.Descendants("GenericProperty").Select(p => p.Element("Alias")!.Value).Order(StringComparer.Ordinal));
                foreach (var blockAlias in new[] { "markdown", "richtexteditor" })
                {
                    var block = XDocument.Load(Path.Combine(ProjectDirectory, $"uSync/v18/ContentTypes/{blockAlias}.config")).Root!;
                    Assert.Empty(block.Descendants("Composition"));
                    Assert.Equal(["content"], block.Descendants("GenericProperty").Select(p => p.Element("Alias")!.Value));
                }
                continue;
            }

            Assert.Empty(root.Descendants("Composition"));
            if (alias == "company")
            {
                Assert.Equal(["aboutText", "availableForHire", "companyTags", "facebookUrl", "featuredEmbed",
                    "featuredImage", "headline", "image", "instagramUrl", "isFoundingSponsor", "isSponsor",
                    "linkedInUrl", "location", "skillTags", "twitterUrl", "umbracoUrlName", "websiteUrl"],
                    properties.Select(p => p.Element("Alias")!.Value).Order(StringComparer.Ordinal));
                Assert.All(properties, p => Assert.Contains(p.Element("Type")!.Value,
                    new[] { "Umbraco.MarkdownEditor", "Umbraco.TrueFalse", "Umbraco.TextBox", "Umbraco.TextArea",
                        "Umbraco.MediaPicker3", "Umbraco.MultiNodeTreePicker" }));
                foreach (var picker in new[] { "CompanyTagsPicker", "SkillTagsPicker" })
                {
                    var config = XDocument.Load(Path.Combine(ProjectDirectory, $"uSync/v18/DataTypes/{picker}.config"));
                    using var json = JsonDocument.Parse(config.Root!.Element("Config")!.Value);
                    Assert.Equal("content", json.RootElement.GetProperty("startNode").GetProperty("type").GetString());
                    Assert.Equal("d3afa3d9-621f-499e-bb8c-e58763df30ef", json.RootElement.GetProperty("filter").GetString());
                }
                continue;
            }
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
