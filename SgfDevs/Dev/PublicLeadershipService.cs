#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using SGFDevs.ViewModels;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.PublishedModels;
using Umbraco.Extensions;

namespace SgfDevs.Dev;

// Leadership stays excluded from Delivery. Only these published selections grant
// access to officer fields; the public member/profile contract stays unchanged.
public class PublicLeadershipService(
    IDocumentUrlService documentUrls,
    IUmbracoContextAccessor contextAccessor,
    PublicContentAccessGuard accessGuard,
    MemberConverter memberConverter,
    PublicMemberService publicMembers)
{
    public async Task<PublicLeadershipDto?> GetAsync()
    {
        var key = documentUrls.GetDocumentKeyByRoute("/about/leadership", null, null, false);
        if (key is null || !contextAccessor.TryGetUmbracoContext(out var context) || context.InPreviewMode) return null;
        if (context.Content?.GetById(false, key.Value) is not Leadership page ||
            !await accessGuard.AllowsAnonymousAsync(page)) return null;
        return new PublicLeadershipDto
        {
            Officers = await ProjectAsync(page.Officers, includeTitle: true, includeBio: true),
            BoardOfDirectors = await ProjectAsync(page.BoardOfDirectors, includeTitle: false, includeBio: false),
            History = await ProjectAsync(page.History, includeTitle: true, includeBio: false)
        };
    }

    private async Task<IReadOnlyList<PublicLeadershipMemberDto>> ProjectAsync(
        IEnumerable<IPublishedContent>? selections, bool includeTitle, bool includeBio)
    {
        var result = new List<PublicLeadershipMemberDto>();
        // Retain picker order, including repeated selections. Legacy history has no year field.
        foreach (var picked in selections ?? [])
        {
            if (picked.ItemType != PublishedItemType.Member || picked.ContentType.Alias != Member.ModelTypeAlias ||
                !await accessGuard.AllowsAnonymousAsync(picked)) continue;
            var member = memberConverter.FromContent(picked);
            // Reuse published member lookup, username normalization and anonymous /member anchor protection.
            var profile = await publicMembers.GetAsync(member.Username);
            if (profile is null) continue;
            result.Add(new PublicLeadershipMemberDto
            {
                Name = member.Name,
                Username = profile.Username,
                ImageUrl = PublicHomeBuilder.GetSafePathOrHttpUrl(member.ProfileImage?.Url()) ?? "/images/pipey.jpg",
                OfficerTitle = includeTitle ? member.OfficerTitle : null,
                OfficerBio = includeBio ? member.OfficerBio?.ToHtmlString() : null
            });
        }
        return result;
    }
}
