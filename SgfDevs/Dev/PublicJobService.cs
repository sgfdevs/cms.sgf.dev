#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using SGFDevs.ViewModels;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.PublishedModels;
using Umbraco.Extensions;

namespace SgfDevs.Dev;

// Native Delivery has neither the legacy root-descendant order nor parent company
// fields. Leave raw job/tag pickers excluded and project only the Razor page values.
public class PublicJobService(
    IDocumentUrlService documentUrls, IUmbracoContextAccessor contexts,
    IDocumentNavigationQueryService navigation, IPublishedStatusFilteringService published,
    IPublishedUrlProvider urls, PublicContentAccessGuard access)
{
    internal static bool IsJobPath(string? path)
    {
        var parts = path?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return path is not null && path.StartsWith("/companies/", StringComparison.Ordinal) &&
            parts is { Length: 3 } && PublicGroupService.IsValidSlug(parts[1]) &&
            PublicGroupService.IsValidSlug(parts[2]) &&
            (path == $"/companies/{parts[1]}/{parts[2]}" || path == $"/companies/{parts[1]}/{parts[2]}/");
    }

    public async Task<IReadOnlyList<PublicJobDto>?> ListAsync()
    {
        var key = documentUrls.GetDocumentKeyByRoute("/jobs", null, null, false);
        if (key is null || !contexts.TryGetUmbracoContext(out var context) || context.InPreviewMode ||
            context.Content?.GetById(false, key.Value) is not Jobs page ||
            !await access.AllowsAnonymousAsync(page)) return null;
        var root = page.Root(navigation, published);
        if (root is null || !await access.AllowsAnonymousAsync(root)) return null;
        var result = new List<PublicJobDto>();
        // Exactly the legacy Root().Descendants<Job>() sequence. No date/type filter,
        // explicit sort, pagination or limit.
        foreach (var job in root.Descendants<Job>(navigation, published))
        {
            var dto = await ProjectAsync(job, false);
            if (dto is not null) result.Add(dto);
        }
        return result;
    }

    public async Task<PublicJobDto?> GetAsync(string? company, string? job)
    {
        if (!PublicGroupService.IsValidSlug(company) || !PublicGroupService.IsValidSlug(job) ||
            !contexts.TryGetUmbracoContext(out var context) || context.InPreviewMode) return null;
        var companyKey = documentUrls.GetDocumentKeyByRoute($"/companies/{company}", null, null, false);
        var jobKey = documentUrls.GetDocumentKeyByRoute($"/companies/{company}/{job}", null, null, false);
        if (companyKey is null || jobKey is null ||
            context.Content?.GetById(false, companyKey.Value) is not Company parent ||
            context.Content.GetById(false, jobKey.Value) is not Job document ||
            document.Parent<Company>(navigation, published)?.Key != parent.Key ||
            !await access.AllowsAnonymousAsync(parent)) return null;
        return await ProjectAsync(document, true);
    }

    internal static string? SafeApplyUrl(string? value)
    {
        if (value is not null && value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal) &&
            !value.Contains('\\') && !value.Contains('%') && !System.Linq.Enumerable.Any(value, c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return value;
        return PublicMemberService.GetSafeHttpUrl(value);
    }

    internal async Task<PublicJobDto?> ProjectAsync(Job job, bool detail)
    {
        var company = job.Parent<Company>(navigation, published);
        if (company is null || !await access.AllowsAnonymousAsync(company) ||
            !await access.AllowsAnonymousAsync(job)) return null;
        var path = job.Url(urls);
        var companyPath = company.Url(urls).TrimEnd('/');
        if (!IsJobPath(path) || !path.StartsWith(companyPath + "/", StringComparison.OrdinalIgnoreCase)) return null;
        var skills = new List<string>();
        if (detail)
            foreach (var tag in job.SkillTags ?? [])
                if (tag is Tag skill && await access.AllowsAnonymousAsync(skill))
                    skills.Add(string.IsNullOrEmpty(skill.DisplayName) ? skill.Name : skill.DisplayName);
        return new PublicJobDto
        {
            Name = job.Name, CompanyName = company.Name, Path = path,
            Location = job.Location, EmploymentType = job.EmploymentType, Compensation = job.Compensation,
            Posted = job.CreateDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture),
            DescriptionHtml = detail ? job.JobDescription?.ToHtmlString() : null,
            ApplyUrl = detail ? SafeApplyUrl(job.ApplyUrl) : null,
            Skills = skills
        };
    }
}
