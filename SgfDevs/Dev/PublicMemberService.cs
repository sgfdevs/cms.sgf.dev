#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using SGFDevs.ViewModels;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.PublishedModels;
using Umbraco.Extensions;
using Tag = Umbraco.Cms.Web.Common.PublishedModels.Tag;

namespace SgfDevs.Dev;

public class PublicMemberService
{
    // Umbraco 18.2's member loginName column is limited to 1000 characters.
    public const int MaxUsernameLength = 1000;
    private const string FallbackImage = "/images/pipey.jpg";
    private readonly IMemberManager _memberManager;
    private readonly IDocumentUrlService _documentUrlService;
    private readonly IUmbracoContextAccessor _contextAccessor;
    private readonly MemberConverter _memberConverter;
    private readonly MemberTagDisplayService _tagDisplayService;
    private readonly PublicContentAccessGuard _accessGuard;

    public PublicMemberService(
        IMemberManager memberManager,
        IDocumentUrlService documentUrlService,
        IUmbracoContextAccessor contextAccessor,
        MemberConverter memberConverter,
        MemberTagDisplayService tagDisplayService,
        PublicContentAccessGuard accessGuard)
    {
        _memberManager = memberManager;
        _documentUrlService = documentUrlService;
        _contextAccessor = contextAccessor;
        _memberConverter = memberConverter;
        _tagDisplayService = tagDisplayService;
        _accessGuard = accessGuard;
    }

    public async Task<PublicMemberProfileDto?> GetAsync(string? username)
    {
        if (!IsValidUsername(username))
        {
            return null;
        }

        // Resolve the same published /member anchor as the legacy virtual page.
        var anchorKey = _documentUrlService.GetDocumentKeyByRoute("/member", null, null, false);
        if (anchorKey is null || !_contextAccessor.TryGetUmbracoContext(out var context))
        {
            return null;
        }

        var anchor = context.Content?.GetById(anchorKey.Value);
        if (anchor is null || !await _accessGuard.AllowsAnonymousAsync(anchor))
        {
            return null;
        }

        // Keep Umbraco Identity's username normalization and case behavior.
        var user = await _memberManager.FindByNameAsync(username!);
        if (user is null || !IsValidUsername(user.UserName))
        {
            return null;
        }

        var publishedMember = _memberManager.AsPublishedMember(user);
        if (publishedMember is null)
        {
            return null;
        }

        var member = _memberConverter.FromContent(publishedMember);
        return await BuildAsync(member, user.UserName!);
    }

    private async Task<PublicMemberProfileDto> BuildAsync(Member member, string username)
    {
        var publicTags = new List<Tag>();
        foreach (var tag in member.MemberTags?.OfType<Tag>() ?? [])
        {
            if (await _accessGuard.AllowsAnonymousAsync(tag))
            {
                publicTags.Add(tag);
            }
        }

        var skills = new List<PublicMemberSkillDto>();
        foreach (var skill in member.SkillsTags?.OfType<Tag>() ?? [])
        {
            if (await _accessGuard.AllowsAnonymousAsync(skill))
            {
                skills.Add(new PublicMemberSkillDto
                {
                    Name = string.IsNullOrEmpty(skill.DisplayName) ? skill.Name : skill.DisplayName,
                    // SGFMemberIndexComponent indexes this GUID string in skillKeys.
                    DirectoryFilterValue = skill.Key.ToString()
                });
            }
        }

        var websiteUrl = GetSafeHttpUrl(member.WebsiteUrl, allowBareWebsite: true);
        var websiteHost = websiteUrl is null ? null : new Uri(websiteUrl).Host;
        var image = member.ProfileImage?.GetCropUrl(width: 800);
        var joinDate = member.GetProperty("CreateDate")?.GetValue() as DateTime?;

        return new PublicMemberProfileDto
        {
            Username = username,
            Name = $"{member.FirstName} {member.LastName}".Trim(),
            FirstName = member.FirstName,
            LastName = member.LastName,
            JobTitle = member.JobTitle,
            ProfileImageUrl = PublicHomeBuilder.GetSafePathOrHttpUrl(image) ?? FallbackImage,
            Tags = _tagDisplayService.GetDisplayMemberTags(publicTags),
            City = member.City,
            State = member.State,
            JoinMonthLabel = joinDate?.ToString("MMMM yyyy", CultureInfo.InvariantCulture),
            AboutHtml = member.AboutText?.ToHtmlString(),
            Skills = skills,
            WebsiteUrl = websiteUrl,
            WebsiteLabel = websiteHost?.StartsWith("www.", StringComparison.OrdinalIgnoreCase) == true
                ? websiteHost[4..] : websiteHost,
            TwitterUrl = GetSafeHttpUrl(member.TwitterUrl),
            LinkedInUrl = GetSafeHttpUrl(member.LinkedInUrl),
            FacebookUrl = GetSafeHttpUrl(member.FacebookUrl),
            InstagramUrl = GetSafeHttpUrl(member.InstagramUrl),
            YouTubeUrl = GetSafeHttpUrl(member.YouTubeUrl),
            AvailableForHire = member.AvailableForHire,
            AvailableForContractWork = member.AvailableForContractWork
        };
    }

    internal static bool IsValidUsername(string? username) =>
        !string.IsNullOrEmpty(username) && username.Length <= MaxUsernameLength &&
        username.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9');

    internal static string? GetSafeHttpUrl(string? value, bool allowBareWebsite = false)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) ||
            value.Contains('\\') || Uri.UnescapeDataString(value).Any(char.IsControl))
        {
            return null;
        }

        if (allowBareWebsite && !value.Contains(':') && !value.StartsWith('/'))
        {
            value = "https://" + value;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo)
            ? uri.AbsoluteUri : null;
    }
}
