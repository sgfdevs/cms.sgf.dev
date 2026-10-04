#nullable enable
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SGFDevs.Controllers;
using SGFDevs.ViewModels;
using SgfDevs.Dev;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.PublishedModels;
using Xunit;

namespace SgfDevs.Tests;

public class PublicJobContractTests
{
    [Fact]
    public async Task MissingPreviewAndProtectedAncestorsNeverReturnFakeSuccess()
    {
        foreach (var mode in new[] { "missing", "preview", "home", "company", "job", "wrong-parent" })
        {
            var f = new Fixture(mode);
            Assert.IsType<NotFoundObjectResult>((await new PublicJobController(f.Service).Get("custom-company", "custom-job")).Result);
            if (mode is "missing" or "preview" or "home") Assert.Null(await f.Service.ListAsync());
            else Assert.Empty((await f.Service.ListAsync())!);
        }
    }

    [Fact]
    public async Task ListingKeepsNativeDescendantOrderWithoutExpirySortOrLimitAndDetailUsesResolvedParent()
    {
        var f = new Fixture();
        var rows = (await f.Service.ListAsync())!;
        Assert.Equal(["Z old job", "A new job"], rows.Select(j => j.Name));
        Assert.Equal("January 2, 2001", rows[0].Posted);
        Assert.All(rows, j => { Assert.Equal("Parent company", j.CompanyName); Assert.Null(j.DescriptionHtml); Assert.Empty(j.Skills); });
        var job = (await f.Service.GetAsync("custom-company", "custom-job"))!;
        Assert.Equal("/companies/custom-company/custom-job/", job.Path);
        Assert.Equal("Remote", job.Location);
        Assert.Equal("Full time", job.EmploymentType);
        Assert.Equal("$90,000", job.Compensation);
        Assert.Equal(["Visible skill", "Fallback skill", "Visible skill"], job.Skills);
        Assert.Equal("<p>Public job description</p>", job.DescriptionHtml);
        Assert.Equal("https://example.test/apply", job.ApplyUrl);
        Assert.DoesNotContain("private-", JsonSerializer.Serialize(job));
    }

    [Fact]
    public async Task InvalidSlugsMissingJobsAndUnsafeApplyKeepNarrowPublicContract()
    {
        var f = new Fixture();
        foreach (var bad in new[] { "", "../private", "a/b", "a%2fb", "a?preview=true", "a\\b" })
            Assert.Null(await f.Service.GetAsync("custom-company", bad));
        Assert.Null(await f.Service.GetAsync("custom-company", "missing-job"));
        Assert.Null((await new Fixture("unsafe-apply").Service.GetAsync("custom-company", "custom-job"))!.ApplyUrl);
        Assert.False(PublicJobService.IsJobPath("/companies/x/y//"));
        Assert.DoesNotContain(typeof(PublicJobDto).GetProperties(), p => p.Name.Contains("Id") || p.Name.Contains("Key") || p.Name.Contains("Tags"));
    }

    private sealed class Fixture
    {
        public PublicJobService Service { get; }
        public Fixture(string mode = "")
        {
            var fallback = Proxy<IPublishedValueFallback>((_, _) => null);
            var homeKey = Guid.NewGuid(); var pageKey = Guid.NewGuid(); var companyKey = Guid.NewGuid();
            var jobKey = Guid.NewGuid(); var nextKey = Guid.NewGuid();
            var home = Content("Home", homeKey, "-1,10", "home");
            var page = new Jobs(Content("Jobs", pageKey, "-1,10,20", "jobs"), fallback);
            var company = new Company(Content("Parent company", companyKey, "-1,10,30,40", "company"), fallback);
            Tag Skill(string name, string display, string path) => new(Content(name, Guid.NewGuid(), path, "tag", new() { ["displayName"] = display }), fallback);
            var skill = Skill("Skill", "Visible skill", "-1,10,80");
            var hidden = Skill("private-name", "private-display", "-1,secret,81");
            var job = new Job(Content("Z old job", jobKey, "-1,10,30,40,50", "job", new() {
                ["location"] = "Remote", ["employmentType"] = "Full time", ["compensation"] = "$90,000",
                ["jobDescription"] = new HtmlEncodedString("<p>Public job description</p>"),
                ["applyURL"] = mode == "unsafe-apply" ? "javascript:private()" : "https://example.test/apply",
                ["skillTags"] = new[] { skill, hidden, Skill("Fallback skill", "", "-1,10,82"), skill },
                ["jobTags"] = new[] { "private-picker" }, ["email"] = "private-email"
            }), fallback);
            var next = new Job(Content("A new job", nextKey, "-1,10,30,40,51", "job"), fallback);
            var documents = new Dictionary<Guid, IPublishedContent> { [homeKey] = home, [pageKey] = page, [companyKey] = company, [jobKey] = job, [nextKey] = next };
            var routes = Proxy<IDocumentUrlService>((_, a) => {
                Assert.False((bool)a![3]!);
                if (mode == "missing") return null;
                return (string)a[0]! switch { "/jobs" => pageKey, "/companies/custom-company" => companyKey,
                    "/companies/custom-company/custom-job" => jobKey, _ => null };
            });
            var cache = Proxy<IPublishedContentCache>((_, a) => { Assert.Equal(false, a![0]); return documents[(Guid)a[1]!]; });
            var context = Proxy<IUmbracoContext>((m, _) => m.Name switch { "get_Content" => cache, "get_InPreviewMode" => mode == "preview", _ => null });
            var accessor = Proxy<IUmbracoContextAccessor>((_, a) => { a![0] = context; return true; });
            var navigation = Proxy<INavigationQueryService>((m, a) => {
                var key = (Guid)a![0]!;
                switch (m.Name) {
                    case "TryGetParentKey": a[1] = key == homeKey ? null : key == pageKey || key == companyKey || mode == "wrong-parent" ? homeKey : companyKey; return true;
                    case "TryGetAncestorsKeys": a[1] = new[] { homeKey }; return true;
                    case "TryGetDescendantsKeys": a[1] = new[] { jobKey, nextKey }; return true;
                    case "TryGetDescendantsKeysOfType": a[2] = new[] { jobKey, nextKey }; return true;
                    default: throw new Exception("Unexpected navigation " + m.Name);
                }
            });
            var published = Proxy<IPublishedStatusFilteringService>((_, a) => ((IEnumerable<Guid>)a![0]!).Select(k => documents[k]));
            var urls = Proxy<IPublishedUrlProvider>((_, a) => {
                var key = a![0] is IPublishedContent c ? c.Key : (Guid)a[0]!;
                return key == companyKey ? "/companies/custom-company/" : key == jobKey ? "/companies/custom-company/custom-job/" : "/companies/custom-company/next-job/";
            });
            var protection = Proxy<IPublicContentProtectionLookup>((_, a) => {
                var path = (string)a![0]!;
                return path.Contains("secret") || mode == "home" && path.Contains(",10") ||
                    mode == "company" && path.Contains(",40") || mode == "job" && (path.EndsWith(",50") || path.EndsWith(",51"));
            });
            var checker = Proxy<IPublicAccessChecker>((_, a) => {
                Assert.False(((ClaimsPrincipal)a![1]!).Identity!.IsAuthenticated);
                return Task.FromResult(PublicAccessStatus.NotLoggedIn);
            });
            Service = new(routes, accessor, navigation, published, urls, new(checker, protection));
        }
        private static IPublishedContent Content(string name, Guid key, string path, string alias, Dictionary<string, object?>? values = null) =>
            Proxy<IPublishedContent>((m, a) => m.Name switch {
                "get_Id" => 900, "get_Key" => key, "get_Path" => path, "get_Name" => name,
                "get_CreateDate" => name.StartsWith("Z") ? new DateTime(2001, 1, 2) : new DateTime(2026, 9, 3),
                "get_ItemType" => PublishedItemType.Content,
                "get_ContentType" => Proxy<IPublishedContentType>((p, _) => p.Name switch { "get_Alias" => alias, "get_ItemType" => PublishedItemType.Content, _ => null }),
                "GetProperty" => values is not null && values.TryGetValue((string)a![0]!, out var v) ? Proxy<IPublishedProperty>((p, _) => p.Name switch {
                    "GetValue" => v, "HasValue" => v is not null, _ => null
                }) : null, _ => null
            });
    }
    public class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args) ??
            (method!.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null);
    }
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class {
        var proxy = DispatchProxy.Create<T, InterfaceProxy>(); ((InterfaceProxy)(object)proxy).Handler = handler; return proxy;
    }
}
