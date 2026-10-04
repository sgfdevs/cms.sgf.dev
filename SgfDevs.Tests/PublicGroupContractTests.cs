#nullable enable
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using SGFDevs.ViewModels;
using SgfDevs.Dev;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.PublishedModels;
using Xunit;

namespace SgfDevs.Tests;

public class PublicGroupContractTests
{
    [Fact]
    public void ListingKeepsChildOrderAndOnlyExcludesExactLegacyName()
    {
        var groups = new[] { "Z group", "Springfield Devs", "A group", "springfield devs" }
            .Select(name => new PublicGroupDto { Name = name });
        Assert.Equal(["Z group", "A group", "springfield devs"], PublicGroupService.ForListing(groups).Select(g => g.Name));
        Assert.True(PublicGroupService.IsValidSlug("Springfield-Devs"));
        foreach (var invalid in new[] { "", "../private", "a/b", "a%2fb", "a?x", "a#x", "-a", "a\\b" })
            Assert.False(PublicGroupService.IsValidSlug(invalid));
    }

    [Fact]
    public async Task MissingOrProtectedRootIsNotFoundAndNeverUsesRequestIdentity()
    {
        foreach (var missing in new[] { false, true })
        {
            var key = Guid.NewGuid();
            var content = Proxy<IPublishedContent>((m, _) => m.Name switch
            {
                "get_Id" => 20, "get_Path" => "-1,10,20", _ => null
            });
            var root = new Groups(content, Proxy<IPublishedValueFallback>((_, _) => null));
            var routes = Proxy<IDocumentUrlService>((m, a) =>
            {
                Assert.Equal("GetDocumentKeyByRoute", m.Name);
                Assert.Equal("/groups", a![0]);
                Assert.False((bool)a[3]!);
                return missing ? null : key;
            });
            var cache = Proxy<IPublishedContentCache>((_, _) => root);
            var context = Proxy<IUmbracoContext>((_, _) => cache);
            var accessor = Proxy<IUmbracoContextAccessor>((_, a) => { a![0] = context; return true; });
            var protection = Proxy<IPublicContentProtectionLookup>((_, a) =>
            {
                Assert.Equal("-1,10,20", a![0]); // includes a protected ancestor
                return true;
            });
            var checker = Proxy<IPublicAccessChecker>((_, a) =>
            {
                Assert.False(((ClaimsPrincipal)a![1]!).Identity!.IsAuthenticated);
                return Task.FromResult(PublicAccessStatus.NotLoggedIn);
            });
            var service = new PublicGroupService(routes, accessor, new PublicContentAccessGuard(checker, protection),
                null!, null!, null!, new EventDisplayService(), TimeProvider.System);
            Assert.Null(await service.ListAsync());
            Assert.Null(await service.GetAsync("springfield-devs"));
        }
    }

    [Fact]
    public void ContractDoesNotHaveRawPickerOrIdentityFields()
    {
        var dto = new PublicGroupDto { Leaders = [new PublicGroupLeaderDto { Name = "Public name", ProfilePath = "/member/Jane" }] };
        var json = JsonSerializer.Serialize(dto, JsonSerializerOptions.Web);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(new[] { "imageUrl", "listLabel", "location", "name", "profilePath", "tags" },
            doc.RootElement.GetProperty("leaders")[0].EnumerateObject().Select(p => p.Name).Order());
        foreach (var type in new[] { typeof(PublicGroupDto), typeof(PublicGroupLeaderDto), typeof(PublicGroupSkillDto), typeof(PublicGroupPresentationDto) })
            Assert.DoesNotContain(type.GetProperties(), p => p.Name.Contains("Id") || p.Name.Contains("Key") || p.Name.Contains("Properties"));
    }

    public class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var value = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)value).Handler = handler;
        return value;
    }
}
