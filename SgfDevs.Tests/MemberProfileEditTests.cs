#nullable enable

using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SGFDevs.Controllers;
using SGFDevs.ViewModels;
using SgfDevs.Dev;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.PropertyEditors;
using System.ComponentModel.DataAnnotations;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Web.Common.Security;
using Umbraco.Extensions;
using Xunit;

namespace SgfDevs.Tests;

public class MemberProfileEditTests
{
    [Fact]
    public async Task AnonymousAndMissingCurrentMemberCannotReadOrWrite()
    {
        var f = new Fixture { Authenticated = false };
        Assert.IsType<UnauthorizedResult>((await f.Controller.Profile()).Result);
        Assert.IsType<UnauthorizedResult>((await f.Controller.Update(new())).Result);
        Assert.Empty(f.Writes);
        f.Authenticated = true;
        f.Current = false;
        Assert.IsType<UnauthorizedResult>((await f.Controller.Update(new())).Result);
        Assert.Empty(f.Writes);
    }

    [Theory]
    [InlineData("memberId")]
    [InlineData("username")]
    [InlineData("roles")]
    [InlineData("profileImagePath")]
    [InlineData("memberTags")]
    public void EditContractRejectsPrivilegedAndUnknownFields(string field)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MemberProfileEditRequest>(
            "{\"" + field + "\":\"injected\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task DuplicateEmailAndInvalidInputNeverWrite()
    {
        var f = new Fixture { DuplicateEmail = true };
        Assert.False(Result(await f.Controller.Update(new() { Email = "duplicate@example.test" })).Succeeded);
        Assert.Empty(f.Writes);
        var bad = Result(await f.Controller.Update(new() { Email = "not email", City = new string('x', 513), WebsiteUrl = "javascript:alert(1)" }));
        Assert.Equal(new[] { "city", "email", "websiteUrl" }, bad.Errors.Keys.Order());
        Assert.Empty(f.Writes);
        bad = Result(await f.Controller.Update(new() { Skills = [Guid.NewGuid().ToString()], Groups = [f.Skill.ToString()] }));
        Assert.Equal(new[] { "groups", "skills" }, bad.Errors.Keys.Order());
        Assert.Empty(f.Writes);
    }

    [Theory]
    [InlineData("https://user:secret@example.test")]
    [InlineData("//example.test")]
    [InlineData("https://example.test/%0dfoo")]
    [InlineData("https://example.test\\evil")]
    [InlineData("mailto:member@example.test")]
    public async Task UnsafeUrlsCannotWrite(string url)
    {
        var f = new Fixture();
        Assert.False(Result(await f.Controller.Update(new() { WebsiteUrl = url })).Succeeded);
        Assert.Empty(f.Writes);
    }

    [Fact]
    public async Task MissingSchemaOrPublishedCatalogFailsWithoutWrites()
    {
        var f = new Fixture();
        f.Options.Missing = true;
        Assert.Equal(503, Assert.IsType<ObjectResult>((await f.Controller.Update(new())).Result).StatusCode);
        f.Options.Missing = false;
        var missing = new Fixture(missingSchema: true);
        Assert.Equal(503, Assert.IsType<ObjectResult>((await missing.Controller.Update(new())).Result).StatusCode);
        Assert.Empty(f.Writes);
    }

    [Fact]
    public async Task SavesOnlyCurrentMembersAllowedFieldsAndRenewsExistingCookie()
    {
        var f = new Fixture();
        var result = Result(await f.Controller.Update(new()
        {
            FirstName = "New", LastName = "Name", Email = "changed@example.test", WebsiteUrl = "example.test/path",
            AboutText = "<script>user-authored</script>\n**markdown**",
            AvailableForHire = true, Skills = [f.Skill.ToString()], Groups = [f.Group.ToString()]
        }));
        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "UpdateAsync", "Save", "Complete", "SignInAsync" }, f.Writes);
        Assert.Equal("changed@example.test", f.Member.Email);
        Assert.Equal("New Name", f.Member.Name);
        Assert.Equal("https://example.test/path", f.Member.GetValue<string>("websiteUrl"));
        Assert.Contains("<script>", f.Member.GetValue<string>("aboutText"));
        Assert.Equal("unchanged", f.Member.GetValue<string>("jobTitle"));
        Assert.Equal("private untouched", f.Member.GetValue<string>("umbracoMemberComments"));
        Assert.Equal("avatar untouched", f.Member.GetValue<string>("profileImage"));
        Assert.Equal(Udi.Create(Constants.UdiEntityType.Document, f.Group).ToString(), f.Member.GetValue<string>("groups"));
        var dto = Assert.IsType<MemberProfileEditDto>(Assert.IsType<OkObjectResult>((await f.Controller.Profile()).Result).Value);
        Assert.Equal("changed@example.test", dto.Values.Email);
        Assert.Null(dto.ProfileImageUrl);
        Assert.Equal(new[] { f.Group.ToString() }, dto.Values.Groups);
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("private untouched", json);
        Assert.DoesNotContain("avatar untouched", json);
    }

    [Fact]
    public async Task CancelledSaveDoesNotCompleteScopeOrRenewCookie()
    {
        var f = new Fixture { CancelSave = true };
        Assert.Equal(503, Assert.IsType<ObjectResult>((await f.Controller.Update(new() { City = "New city" })).Result).StatusCode);
        Assert.DoesNotContain("Complete", f.Writes);
        Assert.DoesNotContain("SignInAsync", f.Writes);
    }

    private static MemberProfileEditResult Result(ActionResult<MemberProfileEditResult> response) =>
        Assert.IsType<MemberProfileEditResult>(Assert.IsType<OkObjectResult>(response.Result).Value);

    private sealed class Options(Guid skill, Guid group) : MemberProfileChoices(null!)
    {
        public bool Missing;
        public override (MemberProfileChoice[] Skills, MemberProfileChoice[] Groups)? Get() => Missing ? null :
            ([new(skill.ToString(), "Synthetic skill")], [new(group.ToString(), "Synthetic interest group")]);
    }

    private sealed class Fixture
    {
        public bool Authenticated = true, Current = true, DuplicateEmail, CancelSave;
        public Guid Skill = Guid.NewGuid(), Group = Guid.NewGuid();
        public Member Member { get; }
        public Options Options { get; }
        public List<string> Writes { get; } = [];
        public MemberProfileController Controller { get; }
        public Fixture(bool missingSchema = false)
        {
            var helper = new DefaultShortStringHelper(new DefaultShortStringHelperConfig());
            var type = new MemberType(helper, -1) { Alias = "Member" };
            foreach (var property in typeof(MemberProfileEditRequest).GetProperties().Where(x => x.PropertyType == typeof(string) && x.Name != "Email"))
            {
                var alias = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
                if (missingSchema && alias == "city") continue;
                type.AddPropertyType(new PropertyType(helper, alias == "aboutText" ? "Umbraco.MarkdownEditor" : "Umbraco.TextBox",
                    alias == "aboutText" ? ValueStorageType.Ntext : ValueStorageType.Nvarchar, alias));
            }
            foreach (var alias in new[] { "skillsTags", "groups", "availableForHire", "availableForContractWork", "profileImage", "umbracoMemberComments" })
                type.AddPropertyType(new PropertyType(helper, "Umbraco.TextBox", ValueStorageType.Nvarchar, alias));
            Member = new Member("Synthetic Member", "synthetic@example.test", "Synthetic", type);
            Member.SetValue("firstName", "Synthetic");
            Member.SetValue("lastName", "Member");
            Member.SetValue("jobTitle", "unchanged");
            Member.SetValue("profileImage", "avatar untouched");
            Member.SetValue("umbracoMemberComments", "private untouched");
            var identity = new MemberIdentityUser { Key = Member.Key, Name = Member.Name, Email = Member.Email, UserName = Member.Username };
            var manager = Proxy<IMemberManager>((method, args) => method.Name switch
            {
                "GetCurrentMemberAsync" => Task.FromResult(Current ? identity : null),
                "FindByEmailAsync" => Task.FromResult(DuplicateEmail ? new MemberIdentityUser { Key = Guid.NewGuid() } : null),
                "UpdateAsync" => Update(identity),
                "AsPublishedMember" => null,
                _ => throw new Exception(method.Name)
            });
            var members = Proxy<IMemberService>((method, _) => method.Name switch
            {
                "GetById" => Member,
                "Save" => Save(),
                _ => throw new Exception(method.Name)
            });
            var scope = Proxy<ICoreScope>((method, _) =>
            {
                if (method.Name == "Complete") { Writes.Add("Complete"); return true; }
                if (method.Name == "Dispose") return null;
                throw new Exception(method.Name);
            });
            var scopes = Proxy<ICoreScopeProvider>((_, _) => scope);
            var signIn = Proxy<IMemberSignInManager>((method, args) =>
            {
                Assert.Equal("SignInAsync", method.Name);
                Assert.Same(identity, args![0]);
                Assert.Equal(true, args[1]);
                Writes.Add(method.Name);
                return Task.CompletedTask;
            });
            var auth = Proxy<IAuthenticationService>((method, args) =>
            {
                Assert.Equal(IdentityConstants.ApplicationScheme, args![1]);
                return Task.FromResult(Authenticated ? AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Synthetic")], IdentityConstants.ApplicationScheme)),
                    new AuthenticationProperties { IsPersistent = true }, IdentityConstants.ApplicationScheme)) : AuthenticateResult.NoResult());
            });
            var services = new ServiceCollection().AddSingleton(auth).BuildServiceProvider();
            Options = new(Skill, Group);
            var dataType = Proxy<IDataType>((_, _) => null);
            var dataTypes = Proxy<IDataTypeService>((_, _) => Task.FromResult<IDataType?>(dataType));
            var valueEditor = Proxy<IDataValueEditor>((_, _) => Array.Empty<ValidationResult>());
            var editors = new PropertyEditorCollection(new DataEditorCollection(() =>
                new[] { "Umbraco.TextBox", "Umbraco.MarkdownEditor" }.Select(alias =>
                    Proxy<IDataEditor>((method, _) => method.Name switch
                    {
                        "get_Alias" => alias,
                        "GetValueEditor" => valueEditor,
                        _ => throw new Exception(method.Name)
                    }))));
            Controller = new(manager, signIn, members, Options, scopes, editors, dataTypes, NullLogger<MemberProfileController>.Instance)
            { ControllerContext = new() { HttpContext = new DefaultHttpContext { RequestServices = services } } };
        }
        private Task<IdentityResult> Update(MemberIdentityUser user)
        {
            Writes.Add("UpdateAsync"); Member.Name = user.Name!; Member.Email = user.Email!;
            return Task.FromResult(IdentityResult.Success);
        }
        private Attempt<OperationResult?> Save()
        {
            Writes.Add("Save");
            return CancelSave ? Attempt.Fail<OperationResult?>(null) : Attempt.Succeed<OperationResult?>(null);
        }
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, MemberLoginTests.InterfaceProxy>();
        ((MemberLoginTests.InterfaceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
