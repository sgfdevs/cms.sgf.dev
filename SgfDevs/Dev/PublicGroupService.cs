#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using SGFDevs.ViewModels;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.PublishedModels;
using Umbraco.Extensions;
using Event = Umbraco.Cms.Web.Common.PublishedModels.Event;

namespace SgfDevs.Dev;

// Group Delivery values include a private member picker. Keep that alias excluded and
// project the existing published models instead of returning raw properties or picker IDs.
public class PublicGroupService(
    IDocumentUrlService documentUrls,
    IUmbracoContextAccessor contextAccessor,
    PublicContentAccessGuard accessGuard,
    MemberConverter memberConverter,
    PublicMemberService publicMembers,
    PublicHomeService publicHome,
    EventDisplayService eventDisplay,
    TimeProvider timeProvider)
{
    internal static bool IsValidSlug([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] string? slug) =>
        !string.IsNullOrEmpty(slug) && slug.Length <= 200 &&
        char.IsAsciiLetterOrDigit(slug[0]) &&
        slug.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    internal static IEnumerable<PublicGroupDto> ForListing(IEnumerable<PublicGroupDto> groups) =>
        groups.Where(g => g.Name != "Springfield Devs");

    private async Task<Groups?> GetRootAsync()
    {
        var key = documentUrls.GetDocumentKeyByRoute("/groups", null, null, false);
        if (key is null || !contextAccessor.TryGetUmbracoContext(out var context)) return null;
        return context.Content?.GetById(key.Value) is Groups root &&
            await accessGuard.AllowsAnonymousAsync(root) ? root : null;
    }

    public async Task<IReadOnlyList<PublicGroupDto>?> ListAsync()
    {
        var root = await GetRootAsync();
        if (root is null) return null;
        var groups = new List<PublicGroupDto>();
        // Published child order is the legacy list order, not alphabetical order.
        foreach (var group in root.Children<Group>())
        {
            if (group.Name == "Springfield Devs" || !await accessGuard.AllowsAnonymousAsync(group)) continue;
            var dto = await BuildAsync(group, includeDetail: false);
            if (dto is not null) groups.Add(dto);
        }
        return ForListing(groups).ToList();
    }

    public async Task<PublicGroupDto?> GetAsync(string? slug)
    {
        if (!IsValidSlug(slug)) return null;
        var root = await GetRootAsync();
        if (root is null || !contextAccessor.TryGetUmbracoContext(out var context)) return null;
        // Let Umbraco resolve the published URL, including umbracoUrlName and case behavior.
        var key = documentUrls.GetDocumentKeyByRoute($"/groups/{slug}", null, null, false);
        if (key is null || context.Content?.GetById(key.Value) is not Group group ||
            group.Parent<Groups>()?.Id != root.Id || !await accessGuard.AllowsAnonymousAsync(group)) return null;
        return await BuildAsync(group, includeDetail: true);
    }

    private async Task<PublicGroupDto?> BuildAsync(Group group, bool includeDetail)
    {
        var path = group.Url();
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2 || segments[0] != "groups" || !IsValidSlug(segments[1])) return null;
        var leaders = new List<PublicGroupLeaderDto>();
        foreach (var picked in group.Leaders ?? [])
        {
            if (!await accessGuard.AllowsAnonymousAsync(picked)) continue;
            var member = memberConverter.FromContent(picked);
            // Reuse public profile lookup for the protected /member anchor, username
            // normalization, published member lookup and visibility-filtered tags.
            var profile = await publicMembers.GetAsync(member.Username);
            if (profile is null) continue;
            leaders.Add(new PublicGroupLeaderDto
            {
                Name = member.Name,
                ListLabel = $"{profile.FirstName} {(string.IsNullOrEmpty(profile.LastName) ? string.Empty : profile.LastName[..1] + ".")}".Trim(),
                Location = $"{profile.City}, {profile.State}",
                ImageUrl = profile.ProfileImageUrl,
                ProfilePath = $"/member/{profile.Username}",
                Tags = profile.Tags
            });
        }
        var skills = new List<PublicGroupSkillDto>();
        if (includeDetail)
        {
            foreach (var tag in group.SkillTags?.OfType<Tag>() ?? [])
            {
                if (!await accessGuard.AllowsAnonymousAsync(tag)) continue;
                var slug = tag.UrlSegment();
                if (!IsValidSlug(slug)) continue;
                skills.Add(new PublicGroupSkillDto
                {
                    Name = string.IsNullOrEmpty(tag.DisplayName) ? tag.Name : tag.DisplayName,
                    Slug = slug
                });
            }
        }
        return new PublicGroupDto
        {
            Name = group.Name, Path = path, AboutHtml = group.AboutText?.ToHtmlString(),
            ImageUrl = PublicHomeBuilder.GetSafePathOrHttpUrl(group.GroupImage?.Url()),
            Location = includeDetail ? group.Location : null,
            EstablishedText = includeDetail ? group.EstablishedText : null,
            WebsiteUrl = PublicMemberService.GetSafeHttpUrl(group.WebsiteUrl),
            TwitterUrl = PublicMemberService.GetSafeHttpUrl(group.TwitterUrl),
            LinkedInUrl = PublicMemberService.GetSafeHttpUrl(group.LinkedInUrl),
            FacebookUrl = PublicMemberService.GetSafeHttpUrl(group.FacebookUrl),
            InstagramUrl = PublicMemberService.GetSafeHttpUrl(group.InstagramUrl),
            YouTubeUrl = PublicMemberService.GetSafeHttpUrl(group.YouTubeUrl),
            Leaders = leaders, Skills = skills,
            UpcomingPresentations = includeDetail ? await UpcomingAsync(group) : []
        };
    }

    private async Task<IReadOnlyList<PublicGroupPresentationDto>> UpcomingAsync(Group group)
    {
        var home = group.AncestorOrSelf<Home>();
        if (home is null || !await accessGuard.AllowsAnonymousAsync(home)) return [];
        var now = eventDisplay.GetCurrentTime(timeProvider.GetUtcNow());
        var result = new List<PublicGroupPresentationDto>();
        var presentations = home.Descendants<Presentation>()
            .Where(p => p.Group?.Id == group.Id)
            .Where(p => p.Parent<Event>() is { } parent && eventDisplay.IsCurrentOrUpcoming(parent.Date, now))
            .OrderBy(p => p.Parent<Event>()!.Date);
        foreach (var presentation in presentations)
        {
            var parent = presentation.Parent<Event>()!;
            if (!await accessGuard.AllowsAnonymousAsync(parent) || !await accessGuard.AllowsAnonymousAsync(presentation)) continue;
            var presenters = await publicHome.BuildPresentersAsync(presentation, 960);
            // The legacy card skips presentations without a visible primary presenter.
            if (presenters.Count == 0) continue;
            result.Add(new PublicGroupPresentationDto
            {
                Title = presentation.Name, EventName = parent.Name,
                StartsAtLocal = parent.Date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                Presenters = presenters
            });
        }
        return result;
    }
}
