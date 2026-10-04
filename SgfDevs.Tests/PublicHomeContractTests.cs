#nullable enable

using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SGFDevs.Controllers;
using SGFDevs.ViewModels;
using SgfDevs.Dev;
using SgfDevs.Dev.EventSync;
using Umbraco.Cms.Core.Security;
using Xunit;

namespace SgfDevs.Tests;

public class PublicHomeContractTests
{
    [Fact]
    public void PublicHomeDto_SerializesOnlyHomeDataFields()
    {
        var dto = new PublicHomeDto
        {
            NextDevNight = new PublicHomeDevNightDto
            {
                Name = "Dev Night October 2026",
                StartsAtLocal = "2026-10-07T18:30:00",
                TimeZone = "America/Chicago",
                DateLabel = "Oct 7, 2026",
                DateTimeAttribute = "10-07-2026 18:30:00",
                Presentations =
                [
                    new PublicHomePresentationDto
                    {
                        Title = "Open Vehicle Monitoring System",
                        MeetupUrl = "https://www.meetup.com/sgfdevs/events/example/",
                        Presenters =
                        [
                            new PublicHomePresenterDto
                            {
                                Name = "Tiffany Ford",
                                ImageUrl = "/media/tiffany.jpg",
                                ProfilePath = "/member/rhysma",
                                Tags = ["2024 Supporting Member"]
                            }
                        ],
                        Group = new PublicHomeGroupDto
                        {
                            Name = "Springfield Devs",
                            Path = "/groups/springfield-devs/",
                            ShowAttribution = false
                        }
                    }
                ]
            },
            Directory = new PublicHomeDirectoryPreviewDto
            {
                TotalMembers = 1,
                DailyMembers =
                [
                    new PublicDirectoryMemberDto
                    {
                        Name = "Jane Developer",
                        Location = "Springfield, MO",
                        Image = "/images/pipey.jpg",
                        Url = "/member/jane",
                        Tags = ["C#"]
                    }
                ]
            },
            Sponsors =
            [
                new PublicHomeSponsorDto
                {
                    Name = "Banno",
                    Path = "/companies/banno/",
                    LogoUrl = "/media/banno.svg",
                    WebsiteUrl = "https://banno.com/",
                    WebsiteLabel = "banno.com",
                    IsFoundingSponsor = true
                }
            ]
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(dto, JsonSerializerOptions.Web));
        var propertyNames = FlattenPropertyNames(document.RootElement).ToArray();

        Assert.Equal(["directory", "nextDevNight", "sponsors"], document.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.DoesNotContain(propertyNames, IsPrivatePropertyName);
        Assert.DoesNotContain("shell", propertyNames);
        Assert.DoesNotContain("nav", propertyNames);
        Assert.DoesNotContain("footer", propertyNames);
    }

    [Theory]
    [InlineData(nameof(PublicHomeController.Get), "api/v1/public/home", "PublicHome_Get", typeof(PublicHomeDto))]
    public void PublicHomeEndpoint_HasStableRouteOperationNameAndResponseType(
        string methodName,
        string routeTemplate,
        string operationName,
        Type responseType)
    {
        var method = typeof(PublicHomeController).GetMethod(methodName)!;
        var route = method.GetCustomAttribute<HttpGetAttribute>()!;
        var okResponse = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == 200);

        Assert.Equal(routeTemplate, route.Template);
        Assert.Equal(operationName, route.Name);
        Assert.Equal(responseType, okResponse.Type);
    }

    private static IEnumerable<string> FlattenPropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (var child in FlattenPropertyNames(property.Value))
                {
                    yield return child;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var child in FlattenPropertyNames(item))
                {
                    yield return child;
                }
            }
        }
    }

    private static bool IsPrivatePropertyName(string propertyName) =>
        propertyName.Contains("email", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("comment", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Contains("key", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Equals("id", StringComparison.OrdinalIgnoreCase) ||
        propertyName.Equals("udi", StringComparison.OrdinalIgnoreCase);
}

public class PublicHomeBuilderTests
{
    [Fact]
    public void BuildNextDevNight_UsesFirstCurrentEventAndDoesNotSkipAheadWhenItHasNoPresentations()
    {
        var builder = CreateBuilder();
        var now = new DateTime(2026, 10, 7, 18, 0, 0);
        var events = new[]
        {
            new PublicHomeEventSource("Earlier empty", new DateTime(2026, 10, 7, 18, 30, 0), []),
            new PublicHomeEventSource("Later with talk", new DateTime(2026, 11, 4, 18, 30, 0), [CreatePresentation("Talk")])
        };

        Assert.Null(builder.BuildNextDevNight(events, now));
    }

    [Fact]
    public void BuildNextDevNight_KeepsCurrentEventInsideOneHourWindowAndFormatsLocalDateFields()
    {
        var builder = CreateBuilder();
        var now = new DateTime(2026, 10, 7, 19, 29, 0);

        var devNight = builder.BuildNextDevNight(
        [
            new PublicHomeEventSource("Expired", new DateTime(2026, 10, 7, 17, 0, 0), [CreatePresentation("Old")]),
            new PublicHomeEventSource("Dev Night October 2026", new DateTime(2026, 10, 7, 18, 30, 0), [CreatePresentation("Current")])
        ], now);

        Assert.NotNull(devNight);
        Assert.Equal("Dev Night October 2026", devNight.Name);
        Assert.Equal("2026-10-07T18:30:00", devNight.StartsAtLocal);
        Assert.Equal("America/Chicago", devNight.TimeZone);
        Assert.Equal("Oct 7, 2026", devNight.DateLabel);
        Assert.Equal("10-07-2026 18:30:00", devNight.DateTimeAttribute);
    }

    [Fact]
    public void GetDailyRandomItems_MatchesLegacyAlgorithmForLargePopulations()
    {
        var members = Enumerable.Range(1, 30).ToList();
        var date = new DateTime(2026, 10, 7);

        Assert.Equal(
            LegacyDailyRandomItems(members, PublicHomeBuilder.DirectoryPreviewSize, date),
            PublicHomeBuilder.GetDailyRandomItems(members, PublicHomeBuilder.DirectoryPreviewSize, date));
    }

    [Fact]
    public void GetDailyRandomItems_DoesNotThrowForSmallPopulations()
    {
        var members = new[] { 1, 2 };
        var result = PublicHomeBuilder.GetDailyRandomItems(members, PublicHomeBuilder.DirectoryPreviewSize, new DateTime(2026, 10, 7));

        Assert.Equal(2, result.Count);
        Assert.Equal([1, 2], result.Order());
    }

    [Fact]
    public void Build_PreservesSponsorTreeOrder()
    {
        var builder = CreateBuilder();
        var dto = builder.Build(
            new PublicHomeContent(
                [],
                [],
                [
                    new PublicHomeSponsorSource("First", "/first/", null, null, null, false),
                    new PublicHomeSponsorSource("Second", "/second/", null, null, null, true)
                ]),
            new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 7, 7, 0, 0, TimeSpan.Zero));

        Assert.Equal(["First", "Second"], dto.Sponsors.Select(sponsor => sponsor.Name));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("//evil.example/path")]
    public void UrlHelpers_BlockUnsafePublicUrls(string url)
    {
        Assert.Null(PublicHomeBuilder.GetSafePathOrHttpUrl(url));
        Assert.Null(PublicHomeBuilder.GetSafeHttpUrl(url));
    }

    [Theory]
    [InlineData("https://www.example.com/", "example.com")]
    [InlineData("http://www.example.com/", "http:example.com")]
    public void GetWebsiteLabel_MatchesLegacySponsorLabelFormatting(string url, string label)
    {
        Assert.Equal(label, PublicHomeBuilder.GetWebsiteLabel(url));
    }

    private static PublicHomeBuilder CreateBuilder() => new(
        new EventDisplayService(TimeZoneInfo.FindSystemTimeZoneById("America/Chicago")),
        Options.Create(new EventSyncOptions { EventTimeZoneId = "America/Chicago" }));

    private static PublicHomePresentationDto CreatePresentation(string title) => new()
    {
        Title = title,
        MeetupUrl = null,
        Presenters = [],
        Group = null
    };

    private static IReadOnlyList<int> LegacyDailyRandomItems(IReadOnlyList<int> list, int count, DateTime localNow)
    {
        var seed = localNow.Year * 10000 + localNow.Month * 100 + localNow.Day;
        var rng = new Random(seed);
        var take = Math.Min(count, list.Count);
        var result = new List<int>(take);
        var listCopy = new List<int>(list);

        for (var i = 0; i < take; i++)
        {
            var randomIndex = rng.Next(i, listCopy.Count);
            result.Add(listCopy[randomIndex]);
            listCopy.RemoveAt(randomIndex);
        }

        return result;
    }
}

public class PublicContentAccessGuardTests
{
    [Fact]
    public async Task AllowsAnonymousContentAsync_AllowsUnprotectedContentWithoutMemberAccessCheck()
    {
        var checker = new RecordingPublicAccessChecker(PublicAccessStatus.NotLoggedIn);
        var guard = new PublicContentAccessGuard(checker, new RecordingProtectionLookup(false));

        Assert.True(await guard.AllowsAnonymousContentAsync(123, "-1,123"));
        Assert.Null(checker.ContentId);
    }

    [Fact]
    public async Task AllowsAnonymousContentAsync_UsesExplicitAnonymousPrincipalForProtectedContent()
    {
        var checker = new RecordingPublicAccessChecker(PublicAccessStatus.AccessAccepted);
        var guard = new PublicContentAccessGuard(checker, new RecordingProtectionLookup(true));
        var allowed = await guard.AllowsAnonymousContentAsync(123, "-1,123");

        Assert.True(allowed);
        Assert.Equal(123, checker.ContentId);
        Assert.NotNull(checker.ClaimsPrincipal);
        Assert.False(checker.ClaimsPrincipal!.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task AllowsAnonymousContentAsync_DeniesProtectedContentForNonAcceptedStatuses()
    {
        var checker = new RecordingPublicAccessChecker(PublicAccessStatus.NotLoggedIn);
        var guard = new PublicContentAccessGuard(checker, new RecordingProtectionLookup(true));

        Assert.False(await guard.AllowsAnonymousContentAsync(456, "-1,456"));
    }

    private sealed class RecordingProtectionLookup : IPublicContentProtectionLookup
    {
        private readonly bool _protected;

        public RecordingProtectionLookup(bool @protected)
        {
            _protected = @protected;
        }

        public bool IsProtectedPath(string contentPath) => _protected;
    }

    private sealed class RecordingPublicAccessChecker : IPublicAccessChecker
    {
        private readonly PublicAccessStatus _status;

        public RecordingPublicAccessChecker(PublicAccessStatus status)
        {
            _status = status;
        }

        public int? ContentId { get; private set; }
        public ClaimsPrincipal? ClaimsPrincipal { get; private set; }

        public Task<PublicAccessStatus> HasMemberAccessToContentAsync(int publishedContentId)
        {
            ContentId = publishedContentId;
            return Task.FromResult(_status);
        }

        public Task<PublicAccessStatus> HasMemberAccessToContentAsync(int publishedContentId, ClaimsPrincipal claimsPrincipal)
        {
            ContentId = publishedContentId;
            ClaimsPrincipal = claimsPrincipal;
            return Task.FromResult(_status);
        }
    }
}
