#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SGFDevs.ViewModels;
using Microsoft.Extensions.Options;
using SgfDevs.Dev.EventSync;

namespace SgfDevs.Dev;

public class PublicHomeBuilder
{
    public const int DirectoryPreviewSize = 15;

    private readonly EventDisplayService _eventDisplayService;
    private readonly string _eventTimeZoneId;

    public PublicHomeBuilder(EventDisplayService eventDisplayService, IOptions<EventSyncOptions> eventSyncOptions)
    {
        _eventDisplayService = eventDisplayService;
        _eventTimeZoneId = eventSyncOptions.Value.EventTimeZoneId;
    }

    public PublicHomeDto Build(PublicHomeContent content, DateTimeOffset utcNow, DateTimeOffset localNow)
    {
        var now = _eventDisplayService.GetCurrentTime(utcNow);
        var nextDevNight = BuildNextDevNight(content.Events, now);
        var dailyMembers = GetDailyRandomItems(content.DirectoryMembers, DirectoryPreviewSize, localNow.DateTime).ToList();

        return new PublicHomeDto
        {
            NextDevNight = nextDevNight,
            Directory = new PublicHomeDirectoryPreviewDto
            {
                TotalMembers = content.DirectoryMembers.Count,
                DailyMembers = dailyMembers
            },
            Sponsors = content.Sponsors
                .Select(source => new PublicHomeSponsorDto
                {
                    Name = source.Name,
                    Path = source.Path,
                    LogoUrl = source.LogoUrl,
                    WebsiteUrl = source.WebsiteUrl,
                    WebsiteLabel = source.WebsiteLabel,
                    IsFoundingSponsor = source.IsFoundingSponsor
                })
                .ToList()
        };
    }

    public PublicHomeDevNightDto? BuildNextDevNight(IReadOnlyList<PublicHomeEventSource> events, DateTime now)
    {
        var nextEvent = _eventDisplayService.GetCurrentAndUpcomingEvents(events, item => item.Date, now).FirstOrDefault();
        if (nextEvent is null || nextEvent.Presentations.Count == 0)
        {
            return null;
        }

        return new PublicHomeDevNightDto
        {
            Name = nextEvent.Name,
            StartsAtLocal = nextEvent.Date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            TimeZone = _eventTimeZoneId,
            DateLabel = nextEvent.Date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture),
            DateTimeAttribute = nextEvent.Date.ToString("MM-dd-yyyy HH:mm:ss", CultureInfo.InvariantCulture),
            Presentations = nextEvent.Presentations
        };
    }

    public static IReadOnlyList<T> GetDailyRandomItems<T>(IReadOnlyList<T> list, int count, DateTime localNow)
    {
        var seed = localNow.Year * 10000 + localNow.Month * 100 + localNow.Day;
        var rng = new Random(seed);
        var take = Math.Min(count, list.Count);
        var result = new List<T>(take);
        var listCopy = new List<T>(list);

        for (var i = 0; i < take && listCopy.Count > 0; i++)
        {
            var randomIndex = i < listCopy.Count
                ? rng.Next(i, listCopy.Count)
                : rng.Next(0, listCopy.Count);

            result.Add(listCopy[randomIndex]);
            listCopy.RemoveAt(randomIndex);
        }

        return result;
    }

    public static string? GetSafeHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Scheme is "http" or "https" ? uri.ToString() : null;
    }

    public static string? GetSafePathOrHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal))
        {
            return url;
        }

        return GetSafeHttpUrl(url);
    }

    public static string? GetWebsiteLabel(string? websiteUrl)
    {
        var safeUrl = GetSafeHttpUrl(websiteUrl);
        return safeUrl?.Replace("https://", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .Replace("www.", string.Empty, StringComparison.Ordinal);
    }
}

public record PublicHomeContent(
    IReadOnlyList<PublicHomeEventSource> Events,
    IReadOnlyList<PublicDirectoryMemberDto> DirectoryMembers,
    IReadOnlyList<PublicHomeSponsorSource> Sponsors);

public record PublicHomeEventSource(
    string Name,
    DateTime Date,
    IReadOnlyList<PublicHomePresentationDto> Presentations);

public record PublicHomeSponsorSource(
    string Name,
    string Path,
    string? LogoUrl,
    string? WebsiteUrl,
    string? WebsiteLabel,
    bool IsFoundingSponsor);
