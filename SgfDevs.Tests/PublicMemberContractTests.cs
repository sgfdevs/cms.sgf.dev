#nullable enable

using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Formatters;
using SGFDevs.Controllers;
using SGFDevs.ViewModels;
using SgfDevs.Dev;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;
using Umbraco.Cms.Web.Common.PublishedModels;
using Xunit;
using Tag = Umbraco.Cms.Web.Common.PublishedModels.Tag;

namespace SgfDevs.Tests;

public class PublicMemberContractTests
{
    [Fact]
    public async Task Get_SerializesExplicitPublicFieldsWithoutPrivateInputSentinels()
    {
        var fixture = new Fixture();
        var response = await new PublicMemberController(fixture.Service).Get("JaNeDeV");
        var dto = Assert.IsType<PublicMemberProfileDto>(Assert.IsType<OkObjectResult>(response.Result).Value);

        Assert.Equal("JaNeDeV", fixture.LookedUpUsername);
        Assert.Equal("JaneDev", dto.Username);
        Assert.Equal("Jane Example", dto.Name);
        Assert.Equal("Developer", dto.JobTitle);
        Assert.Equal("Springfield", dto.City);
        Assert.Equal("MO", dto.State);
        Assert.Equal("October 2024", dto.JoinMonthLabel);
        Assert.Equal("/images/pipey.jpg", dto.ProfileImageUrl);
        Assert.Equal(["2024-2025 Supporting Member"], dto.Tags);
        Assert.Equal("C#", Assert.Single(dto.Skills).Name);
        Assert.Equal(Fixture.SkillKey.ToString(), dto.Skills[0].DirectoryFilterValue);
        Assert.Equal("https://www.example.test/about", dto.WebsiteUrl);
        Assert.Equal("example.test", dto.WebsiteLabel);
        Assert.Equal("https://social.example.test/jane", dto.TwitterUrl);
        Assert.Null(dto.LinkedInUrl);
        Assert.Null(dto.FacebookUrl);
        Assert.Null(dto.InstagramUrl);
        Assert.Null(dto.YouTubeUrl);
        Assert.True(dto.AvailableForHire);
        Assert.True(dto.AvailableForContractWork);
        Assert.Equal("<p>User biography</p>", dto.AboutHtml);

        var json = JsonSerializer.Serialize(dto, JsonSerializerOptions.Web);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(new[]
        {
            "aboutHtml", "availableForContractWork", "availableForHire", "city", "facebookUrl",
            "firstName", "instagramUrl", "jobTitle", "joinMonthLabel", "lastName", "linkedInUrl",
            "name", "profileImageUrl", "skills", "state", "tags", "twitterUrl", "username",
            "websiteLabel", "websiteUrl", "youTubeUrl"
        }.Order(), document.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(["directoryFilterValue", "name"], document.RootElement.GetProperty("skills")[0]
            .EnumerateObject().Select(p => p.Name).Order());
        foreach (var sentinel in Fixture.PrivateSentinels)
        {
            Assert.DoesNotContain(sentinel, json, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("jane-dev")]
    [InlineData("jane dev")]
    [InlineData("Jänedev")]
    [InlineData("jane/dev")]
    public async Task Get_InvalidUsernameReturns404WithoutLookingUpMember(string? username)
    {
        var fixture = new Fixture();
        var result = await new PublicMemberController(fixture.Service).Get(username!);
        Assert404(result);
        Assert.Null(fixture.LookedUpUsername);
    }

    [Fact]
    public async Task Get_UsernameBoundMatchesInstalledMemberSchema()
    {
        Assert.Equal(PublicMemberService.MaxUsernameLength,
            typeof(LengthAttribute).Assembly.GetType("Umbraco.Cms.Infrastructure.Persistence.Dtos.MemberDto")!
                .GetProperty("LoginName")!.GetCustomAttribute<LengthAttribute>()!.Length);
        var fixture = new Fixture();
        Assert404(await new PublicMemberController(fixture.Service).Get(new string('a', PublicMemberService.MaxUsernameLength + 1)));
        Assert.Null(fixture.LookedUpUsername);
        Assert.True(PublicMemberService.IsValidUsername(new string('a', PublicMemberService.MaxUsernameLength)));
    }

    [Fact]
    public async Task Get_MissingMemberReturns404()
    {
        var fixture = new Fixture { MissingMember = true };
        Assert404(await new PublicMemberController(fixture.Service).Get("Missing123"));
        Assert.Equal("Missing123", fixture.LookedUpUsername);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Get_MissingOrProtectedAnchorReturns404BeforeMemberLookup(bool missing, bool protectedAnchor)
    {
        var fixture = new Fixture { MissingAnchor = missing, ProtectedAnchor = protectedAnchor };
        Assert404(await new PublicMemberController(fixture.Service).Get("JaneDev"));
        Assert.Null(fixture.LookedUpUsername);
        if (protectedAnchor)
        {
            Assert.NotNull(fixture.CheckedPrincipal);
            Assert.False(fixture.CheckedPrincipal.Identity!.IsAuthenticated);
        }
    }

    [Fact]
    public async Task Get_DoesNotRequireDirectoryLocationOrExposeProtectedTagsAndSkills()
    {
        var fixture = new Fixture { ProtectTags = true };
        fixture.Properties["city"] = null;
        fixture.Properties["state"] = null;
        fixture.Properties.Remove("CreateDate");
        var dto = await fixture.Service.GetAsync("JaneDev");
        Assert.NotNull(dto);
        Assert.Null(dto.City);
        Assert.Null(dto.State);
        Assert.Null(dto.JoinMonthLabel);
        Assert.Empty(dto.Tags);
        Assert.Empty(dto.Skills);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hi")]
    [InlineData("//evil.example/path")]
    [InlineData("https://user:secret@example.test/")]
    [InlineData("https://example.test/\npath")]
    [InlineData("https://example.test/%0dpath")]
    [InlineData("https:\\example.test/")]
    public void Urls_RejectUnsafeLinksForWebsiteAndSocials(string value)
    {
        Assert.Null(PublicMemberService.GetSafeHttpUrl(value));
        Assert.Null(PublicMemberService.GetSafeHttpUrl(value, allowBareWebsite: true));
    }

    [Theory]
    [InlineData("http://example.test/path", "http://example.test/path")]
    [InlineData("HTTPS://example.test/", "https://example.test/")]
    public void Urls_KeepHttpAndHttps(string value, string expected) =>
        Assert.Equal(expected, PublicMemberService.GetSafeHttpUrl(value));

    [Fact]
    public void Endpoint_HasOnly200JsonAnd404ProblemMetadata()
    {
        var method = typeof(PublicMemberController).GetMethod(nameof(PublicMemberController.Get))!;
        var route = method.GetCustomAttribute<HttpGetAttribute>()!;
        Assert.Equal("api/v1/public/members/{username}", route.Template);
        Assert.Equal("PublicMember_Get", route.Name);
        Assert.Empty(method.GetCustomAttributes<ProducesAttribute>());
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().OrderBy(a => a.StatusCode).ToArray();
        Assert.Equal([200, 404], responses.Select(a => a.StatusCode));
        Assert.Equal(typeof(PublicMemberProfileDto), responses[0].Type);
        Assert.Equal(typeof(ProblemDetails), responses[1].Type);
        Assert.Equal(["application/json"], ContentTypes(responses[0]));
        Assert.Equal(["application/problem+json"], ContentTypes(responses[1]));
    }

    private static string[] ContentTypes(ProducesResponseTypeAttribute attribute)
    {
        var types = new MediaTypeCollection();
        ((IApiResponseMetadataProvider)attribute).SetContentTypes(types);
        return types.ToArray();
    }

    private static void Assert404(ActionResult<PublicMemberProfileDto> result)
    {
        var problem = Assert.IsType<ProblemDetails>(Assert.IsType<NotFoundObjectResult>(result.Result).Value);
        Assert.Equal(404, problem.Status);
        Assert.Null(problem.Detail);
        Assert.Empty(problem.Extensions);
    }

    private sealed class Fixture
    {
        public static readonly Guid SkillKey = Guid.Parse("11111111-2222-3333-4444-555555555555");
        public static readonly string[] PrivateSentinels =
        ["private-email@example.test", "private-reset-token", "private-password-answer", "private-comments", "private-officer-bio",
            "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", "private-security-stamp"];
        public Dictionary<string, object?> Properties { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["firstName"] = "Jane", ["lastName"] = "Example", ["jobTitle"] = "Developer",
            ["city"] = "Springfield", ["state"] = "MO", ["CreateDate"] = new DateTime(2024, 10, 1),
            ["aboutText"] = new HtmlEncodedString("<p>User biography</p>"),
            ["websiteUrl"] = "www.example.test/about", ["twitterUrl"] = "https://social.example.test/jane",
            ["linkedInUrl"] = "javascript:alert(1)", ["facebookUrl"] = "https://user:secret@example.test/",
            ["instagramUrl"] = "data:text/html,hi", ["youTubeUrl"] = "https://example.test/\n",
            ["availableForHire"] = true, ["availableForContractWork"] = true,
            ["email"] = PrivateSentinels[0], ["resetPasswordToken"] = PrivateSentinels[1],
            ["umbracoMemberPasswordRetrievalAnswer"] = PrivateSentinels[2], ["umbracoMemberComments"] = PrivateSentinels[3],
            ["officerBio"] = new HtmlEncodedString(PrivateSentinels[4])
        };
        public PublicMemberService Service { get; }
        public bool MissingMember { get; init; }
        public bool MissingAnchor { get; init; }
        public bool ProtectedAnchor { get; init; }
        public bool ProtectTags { get; init; }
        public string? LookedUpUsername { get; private set; }
        public ClaimsPrincipal? CheckedPrincipal { get; private set; }

        public Fixture()
        {
            var fallback = Proxy<IPublishedValueFallback>((_, _) => null);
            Tag CreateTag(string name, Guid key) => new(Content(new Dictionary<string, object?> { ["displayName"] = name }, key, "-1,200"), fallback);
            Properties["memberTags"] = new[] { CreateTag("2024 Supporting Member", Guid.NewGuid()), CreateTag("2025 Supporting Member", Guid.NewGuid()) };
            Properties["skillsTags"] = new[] { CreateTag("C#", SkillKey) };
            var member = Content(Properties, Guid.Parse(PrivateSentinels[5]), "-1,999");
            var anchorKey = Guid.NewGuid();
            var anchor = Content([], anchorKey, "-1,100");
            var contentCache = Proxy<IPublishedContentCache>((method, args) =>
            {
                Assert.Equal("GetById", method.Name);
                Assert.Contains(anchorKey, args!);
                return MissingAnchor ? null : anchor;
            });
            var context = Proxy<IUmbracoContext>((method, _) => method.Name == "get_Content" ? contentCache : null);
            var accessor = Proxy<IUmbracoContextAccessor>((method, args) =>
            {
                Assert.Equal("TryGetUmbracoContext", method.Name);
                args![0] = context;
                return true;
            });
            var routes = Proxy<IDocumentUrlService>((method, args) =>
            {
                Assert.Equal("GetDocumentKeyByRoute", method.Name);
                Assert.Equal("/member", args![0]);
                Assert.Equal(false, args[3]);
                return MissingAnchor ? null : anchorKey;
            });
            var manager = Proxy<IMemberManager>((method, args) =>
            {
                if (method.Name == "FindByNameAsync")
                {
                    LookedUpUsername = (string)args![0]!;
                    return Task.FromResult(MissingMember ? null : new MemberIdentityUser
                    {
                        UserName = "JaneDev", Email = PrivateSentinels[0], SecurityStamp = PrivateSentinels[6]
                    });
                }
                Assert.Equal("AsPublishedMember", method.Name);
                return member;
            });
            var protection = Proxy<IPublicContentProtectionLookup>((_, args) =>
                args![0]!.Equals("-1,100") ? ProtectedAnchor : ProtectTags);
            var checker = Proxy<IPublicAccessChecker>((_, args) =>
            {
                CheckedPrincipal = Assert.IsType<ClaimsPrincipal>(args![1]);
                return Task.FromResult(PublicAccessStatus.NotLoggedIn);
            });
            Service = new PublicMemberService(manager, routes, accessor,
                new MemberConverter(fallback, Proxy<IPublishedMemberCache>((_, _) => null)),
                new MemberTagDisplayService(), new PublicContentAccessGuard(checker, protection));
        }

        private static IPublishedContent Content(Dictionary<string, object?> values, Guid key, string path) =>
            Proxy<IPublishedContent>((method, args) => method.Name switch
            {
                "get_Key" => key,
                "get_Id" => 999,
                "get_Path" => path,
                "get_Name" => "Private content name must not be exposed",
                "GetProperty" => values.TryGetValue((string)args![0]!, out var value)
                    ? Proxy<IPublishedProperty>((m, _) => m.Name switch
                    {
                        "GetValue" => value, "HasValue" => value is not null, _ => null
                    }) : null,
                _ => null
            });
    }

    // Handwritten interface doubles, without a mocking package or a running CMS.
    public class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            var result = Handler(method!, args);
            return result ?? (method!.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null);
        }
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
