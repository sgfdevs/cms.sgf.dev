#nullable enable
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using SGFDevs.Controllers;
using SGFDevs.ViewModels;
using SgfDevs.Dev;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.PublishedModels;
using Xunit;

namespace SgfDevs.Tests;

public class PublicLeadershipContractTests
{
    [Fact]
    public async Task MissingProtectedAncestorOrPreviewPageReturns404BeforeMemberLookup()
    {
        foreach (var mode in new[] { "missing", "protected", "preview" })
        {
            var fixture = new Fixture(mode);
            var response = await new PublicLeadershipController(fixture.Service).Get();
            Assert.IsType<NotFoundObjectResult>(response.Result);
            Assert.Equal(0, fixture.MemberLookups);
        }
    }

    [Fact]
    public async Task OnlySelectedMembersKeepPickerOrderAndHistoryTitleWithoutInventedYears()
    {
        var fixture = new Fixture();
        var dto = Assert.IsType<PublicLeadershipDto>(await fixture.Service.GetAsync());
        Assert.Equal(["Z public name", "A public name"], dto.Officers.Select(m => m.Name));
        Assert.Equal(["A public name", "Z public name"], dto.BoardOfDirectors.Select(m => m.Name));
        Assert.Equal(["Z public name", "A public name", "Z public name"], dto.History.Select(m => m.Name));
        Assert.Equal("President", dto.Officers[0].OfficerTitle);
        Assert.Equal("<p>Public officer biography</p>", dto.Officers[0].OfficerBio);
        Assert.Equal("Jane", dto.Officers[0].Username);
        Assert.Equal("/images/pipey.jpg", dto.Officers[0].ImageUrl);
        Assert.Null(dto.BoardOfDirectors[0].OfficerTitle);
        Assert.All(dto.BoardOfDirectors.Concat(dto.History), m => Assert.Null(m.OfficerBio));
        Assert.Equal("", dto.History[1].OfficerTitle); // renderer retains legacy Board Member fallback
        Assert.DoesNotContain("private-", JsonSerializer.Serialize(dto));
    }

    [Fact]
    public async Task ProtectedMemberAnchorOrMissingPublicMemberOmitsSelectionsAndContractIsExplicit()
    {
        foreach (var mode in new[] { "anchor", "member" })
        {
            var dto = Assert.IsType<PublicLeadershipDto>(await new Fixture(mode).Service.GetAsync());
            Assert.Empty(dto.Officers);
            Assert.Empty(dto.BoardOfDirectors);
            Assert.Empty(dto.History);
        }
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(await new Fixture().Service.GetAsync(), JsonSerializerOptions.Web));
        Assert.Equal(["boardOfDirectors", "history", "officers"], json.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(["imageUrl", "name", "officerBio", "officerTitle", "username"],
            json.RootElement.GetProperty("officers")[0].EnumerateObject().Select(p => p.Name).Order());
    }

    private sealed class Fixture
    {
        public PublicLeadershipService Service { get; }
        public int MemberLookups { get; private set; }
        public Fixture(string mode = "")
        {
            var fallback = Proxy<IPublishedValueFallback>((_, _) => null);
            IPublishedContent Selected(string name, string title, string path = "-1,900", PublishedItemType type = PublishedItemType.Member) =>
                Content(new() { ["username"] = "Jane", ["officerTitle"] = title,
                    ["officerBio"] = new HtmlEncodedString("<p>Public officer biography</p>"),
                    ["availableForHire"] = false, ["availableForContractWork"] = false,
                    ["email"] = "private-email", ["securityStamp"] = "private-security" }, name, path, type);
            var z = Selected("Z public name", "President");
            var a = Selected("A public name", "");
            var protectedMember = Selected("private-protected", "private-title", "-1,secret,900");
            var wrongType = Selected("private-document", "private-title", type: PublishedItemType.Content);
            var page = new Leadership(Content(new() {
                ["officers"] = new[] { z, protectedMember, wrongType, a },
                ["boardOfDirectors"] = new[] { a, z }, ["history"] = new[] { z, a, z }
            }, "Leadership", "-1,10,20"), fallback);
            var anchor = Content([], "Member", "-1,10,30");
            var pageKey = Guid.NewGuid();
            var anchorKey = Guid.NewGuid();
            var routes = Proxy<IDocumentUrlService>((_, args) => {
                Assert.False((bool)args![3]!);
                return (string)args[0]! == "/member" ? anchorKey : mode == "missing" ? null : pageKey;
            });
            var cache = Proxy<IPublishedContentCache>((_, args) => {
                if (args!.Contains(pageKey)) {
                    Assert.Equal(false, args![0]); // explicitly published cache, never preview
                    return page;
                }
                return anchor;
            });
            var context = Proxy<IUmbracoContext>((m, _) => m.Name switch {
                "get_Content" => cache, "get_InPreviewMode" => mode == "preview", _ => null
            });
            var accessor = Proxy<IUmbracoContextAccessor>((_, args) => { args![0] = context; return true; });
            var protection = Proxy<IPublicContentProtectionLookup>((_, args) =>
                ((string)args![0]!).Contains("secret") || mode == "protected" && (string)args[0]! == "-1,10,20" ||
                mode == "anchor" && (string)args[0]! == "-1,10,30");
            var checker = Proxy<IPublicAccessChecker>((_, args) => {
                Assert.False(((ClaimsPrincipal)args![1]!).Identity!.IsAuthenticated);
                return Task.FromResult(PublicAccessStatus.NotLoggedIn);
            });
            var guard = new PublicContentAccessGuard(checker, protection);
            var converter = new MemberConverter(fallback, Proxy<IPublishedMemberCache>((_, _) => null));
            var manager = Proxy<IMemberManager>((m, _) => {
                if (m.Name == "FindByNameAsync") {
                    MemberLookups++;
                    return Task.FromResult(mode == "member" ? null : new MemberIdentityUser { UserName = "Jane" });
                }
                return z;
            });
            var members = new PublicMemberService(manager, routes, accessor, converter, new MemberTagDisplayService(), guard);
            Service = new PublicLeadershipService(routes, accessor, guard, converter, members);
        }
        private static IPublishedContent Content(Dictionary<string, object?> values, string name, string path,
            PublishedItemType type = PublishedItemType.Content) => Proxy<IPublishedContent>((m, args) => m.Name switch {
                "get_Id" => 900, "get_Path" => path, "get_Name" => name, "get_ItemType" => type,
                "get_ContentType" => Proxy<IPublishedContentType>((p, _) => p.Name == "get_Alias" ? Member.ModelTypeAlias : null),
                "GetProperty" => values.TryGetValue((string)args![0]!, out var v) ? Proxy<IPublishedProperty>((p, _) => p.Name switch {
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
        var proxy = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
