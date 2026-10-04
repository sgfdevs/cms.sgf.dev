#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SGFDevs.Dev;
using SGFDevs.ViewModels;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Web.Common;
using Umbraco.Cms.Web.Common.PublishedModels;
using Umbraco.Extensions;
using Event = Umbraco.Cms.Web.Common.PublishedModels.Event;
using Member = Umbraco.Cms.Web.Common.PublishedModels.Member;

namespace SgfDevs.Dev;

public class PublicHomeService
{
    private const string FallbackMemberImage = "/images/pipey.jpg";

    private readonly UmbracoHelper _umbracoHelper;
    private readonly DirectoryHelper _directoryHelper;
    private readonly MemberConverter _memberConverter;
    private readonly MemberTagDisplayService _memberTagDisplayService;
    private readonly PublicContentAccessGuard _publicContentAccessGuard;
    private readonly PublicHomeBuilder _publicHomeBuilder;
    private readonly TimeProvider _timeProvider;

    public PublicHomeService(
        UmbracoHelper umbracoHelper,
        DirectoryHelper directoryHelper,
        MemberConverter memberConverter,
        MemberTagDisplayService memberTagDisplayService,
        PublicContentAccessGuard publicContentAccessGuard,
        PublicHomeBuilder publicHomeBuilder,
        TimeProvider timeProvider)
    {
        _umbracoHelper = umbracoHelper;
        _directoryHelper = directoryHelper;
        _memberConverter = memberConverter;
        _memberTagDisplayService = memberTagDisplayService;
        _publicContentAccessGuard = publicContentAccessGuard;
        _publicHomeBuilder = publicHomeBuilder;
        _timeProvider = timeProvider;
    }

    public async Task<PublicHomeResult> GetHomeAsync()
    {
        var homeRoots = _umbracoHelper.ContentAtRoot()
            .OfType<Home>()
            .OrderBy(root => root.SortOrder)
            .ThenBy(root => root.Id)
            .ToList();

        if (homeRoots.Count == 0)
        {
            return PublicHomeResult.NotFound();
        }

        if (homeRoots.Count > 1)
        {
            return PublicHomeResult.Misconfigured();
        }

        var home = homeRoots[0];
        if (!await _publicContentAccessGuard.AllowsAnonymousAsync(home))
        {
            return PublicHomeResult.NotFound();
        }

        var content = new PublicHomeContent(
            await BuildEventsAsync(home),
            BuildDirectoryMembers(),
            await BuildSponsorsAsync(home));

        var utcNow = _timeProvider.GetUtcNow();
        var dto = _publicHomeBuilder.Build(content, utcNow, _timeProvider.GetLocalNow());
        return PublicHomeResult.Ok(dto);
    }

    private async Task<IReadOnlyList<PublicHomeEventSource>> BuildEventsAsync(Home home)
    {
        var events = new List<PublicHomeEventSource>();

        foreach (var devNight in home.Descendants<Event>())
        {
            if (!await _publicContentAccessGuard.AllowsAnonymousAsync(devNight))
            {
                continue;
            }

            var presentations = await BuildPresentationsAsync(devNight);
            events.Add(new PublicHomeEventSource(devNight.Name, devNight.Date, presentations));
        }

        return events;
    }

    private async Task<IReadOnlyList<PublicHomePresentationDto>> BuildPresentationsAsync(Event devNight)
    {
        var presentations = new List<PublicHomePresentationDto>();

        foreach (var presentation in devNight.Children<Presentation>())
        {
            if (!await _publicContentAccessGuard.AllowsAnonymousAsync(presentation))
            {
                continue;
            }

            presentations.Add(new PublicHomePresentationDto
            {
                Title = presentation.Name,
                MeetupUrl = PublicHomeBuilder.GetSafeHttpUrl(presentation.MeetupUrl),
                Presenters = await BuildPresentersAsync(presentation),
                Group = await BuildGroupAsync(presentation.Group)
            });
        }

        return presentations;
    }

    internal async Task<IReadOnlyList<PublicHomePresenterDto>> BuildPresentersAsync(Presentation presentation, int imageWidth = 500)
    {
        var presenters = new List<PublicHomePresenterDto>();

        if (presentation.Presenters == null)
        {
            return presenters;
        }

        foreach (var block in presentation.Presenters)
        {
            switch (block.Content)
            {
                case PresenterPicker presenterPicker when presenterPicker.Member != null:
                {
                    if (!await _publicContentAccessGuard.AllowsAnonymousAsync(presenterPicker.Member))
                    {
                        continue;
                    }

                    var member = _memberConverter.FromContent(presenterPicker.Member);
                    var username = member.Username?.ToLowerInvariant() ?? string.Empty;
                    var image = member.ProfileImage?.GetCropUrl(width: imageWidth) ?? FallbackMemberImage;
                    presenters.Add(new PublicHomePresenterDto
                    {
                        Name = member.Name,
                        ImageUrl = PublicHomeBuilder.GetSafePathOrHttpUrl(image) ?? FallbackMemberImage,
                        ProfilePath = PublicHomeBuilder.GetSafePathOrHttpUrl($"/member/{username}"),
                        Tags = _memberTagDisplayService.GetDisplayMemberTags(member)
                    });
                    break;
                }
                case NonMemberPresenter nonMemberPresenter:
                {
                    var image = nonMemberPresenter.ProfileImage?.GetCropUrl(width: imageWidth) ?? FallbackMemberImage;
                    presenters.Add(new PublicHomePresenterDto
                    {
                        Name = nonMemberPresenter.PresenterName ?? "Presenter",
                        ImageUrl = PublicHomeBuilder.GetSafePathOrHttpUrl(image) ?? FallbackMemberImage,
                        ProfilePath = null,
                        Tags = []
                    });
                    break;
                }
            }
        }

        return presenters;
    }

    private async Task<PublicHomeGroupDto?> BuildGroupAsync(IPublishedContent? groupContent)
    {
        if (groupContent is not Group group || !await _publicContentAccessGuard.AllowsAnonymousAsync(group))
        {
            return null;
        }

        var path = PublicHomeBuilder.GetSafePathOrHttpUrl(group.Url());
        if (path is null)
        {
            return null;
        }

        return new PublicHomeGroupDto
        {
            Name = group.Name,
            Path = path,
            ShowAttribution = !group.Name.Equals("Springfield Devs", StringComparison.OrdinalIgnoreCase)
        };
    }

    private IReadOnlyList<PublicDirectoryMemberDto> BuildDirectoryMembers()
    {
        return _directoryHelper.GetAllMembers()
            .Select(BuildDirectoryMember)
            .ToList();
    }

    private PublicDirectoryMemberDto BuildDirectoryMember(IMember umbracoMember)
    {
        var member = _memberConverter.FromMember(umbracoMember);
        return BuildDirectoryMember(member, 250);
    }

    private PublicDirectoryMemberDto BuildDirectoryMember(Member member, int imageWidth)
    {
        var image = member.ProfileImage?.GetCropUrl(width: imageWidth) ?? FallbackMemberImage;

        return new PublicDirectoryMemberDto
        {
            Name = member.Name,
            Location = $"{member.City}, {member.State}",
            Image = PublicHomeBuilder.GetSafePathOrHttpUrl(image) ?? FallbackMemberImage,
            Url = $"/member/{member.Username}",
            Tags = _memberTagDisplayService.GetDisplayMemberTags(member)
        };
    }

    private async Task<IReadOnlyList<PublicHomeSponsorSource>> BuildSponsorsAsync(Home home)
    {
        var sponsors = new List<PublicHomeSponsorSource>();

        foreach (var company in home.Descendants<Company>().Where(company => company.IsSponsor))
        {
            if (!await _publicContentAccessGuard.AllowsAnonymousAsync(company))
            {
                continue;
            }

            var path = PublicHomeBuilder.GetSafePathOrHttpUrl(company.Url());
            if (path is null)
            {
                continue;
            }

            var websiteUrl = PublicHomeBuilder.GetSafeHttpUrl(company.WebsiteUrl);
            sponsors.Add(new PublicHomeSponsorSource(
                company.Name,
                path,
                PublicHomeBuilder.GetSafePathOrHttpUrl(company.Image?.Url()),
                websiteUrl,
                PublicHomeBuilder.GetWebsiteLabel(websiteUrl),
                company.IsFoundingSponsor));
        }

        return sponsors;
    }
}

public enum PublicHomeResultStatus
{
    Ok,
    NotFound,
    Misconfigured
}

public record PublicHomeResult(PublicHomeResultStatus Status, PublicHomeDto? Home)
{
    public static PublicHomeResult Ok(PublicHomeDto home) => new(PublicHomeResultStatus.Ok, home);
    public static PublicHomeResult NotFound() => new(PublicHomeResultStatus.NotFound, null);
    public static PublicHomeResult Misconfigured() => new(PublicHomeResultStatus.Misconfigured, null);
}
