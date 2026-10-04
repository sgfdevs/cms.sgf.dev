#nullable enable

using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using SGFDevs.Controllers;
using SGFDevs.Models;
using SGFDevs.ViewModels;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Web.Common.Security;
using Umbraco.Extensions;
using Xunit;

namespace SgfDevs.Tests;

public class MemberRegistrationTests
{
    private static MemberRegistrationRequest Valid() => new()
    {
        FirstName = "Synthetic", LastName = "Member", Email = "synthetic@example.test",
        Username = "Synthetic", Password = "Fictional9!", ChallengeQuestion = "SGF"
    };

    [Fact]
    public async Task ValidationReusesSharedRulesAndExactChallengeWithoutCallingServices()
    {
        var f = new Fixture();
        var request = Valid();
        request.Username = "not allowed";
        request.Password = "weak";
        request.ChallengeQuestion = "Sgf";
        var response = Assert.IsType<OkObjectResult>((await f.Controller.Register(request)).Result);
        var result = Assert.IsType<MemberRegistrationResult>(response.Value);
        Assert.False(result.Succeeded);
        Assert.Equal(PasswordValidationRules.ErrorMessage, Assert.Single(result.Errors["password"]));
        Assert.Equal("Better luck next time.", Assert.Single(result.Errors["challengeQuestion"]));
        Assert.True(result.Errors.ContainsKey("username"));
        Assert.Empty(f.Calls);
        Assert.DoesNotContain("weak", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("properties")]
    [InlineData("group")]
    public async Task MissingPrerequisitesNeverCreateAnAccount(string missing)
    {
        var f = new Fixture { Missing = missing };
        var response = Assert.IsType<ObjectResult>((await f.Controller.Register(Valid())).Result);
        Assert.Equal(503, response.StatusCode);
        Assert.DoesNotContain("CreateAsync", f.Calls);
    }

    [Theory]
    [InlineData("email")]
    [InlineData("username")]
    public async Task DuplicateFieldsStaySafeAndDoNotCreate(string field)
    {
        var f = new Fixture { Duplicate = field };
        var result = Assert.IsType<MemberRegistrationResult>(Assert.IsType<OkObjectResult>((await f.Controller.Register(Valid())).Result).Value);
        Assert.False(result.Succeeded);
        Assert.True(result.Errors.ContainsKey(field));
        Assert.DoesNotContain("CreateAsync", f.Calls);
    }

    [Fact]
    public async Task PersistsApprovedMemberFieldsAndGroupBeforePersistentSignIn()
    {
        var f = new Fixture();
        var request = Valid();
        request.ChallengeQuestion = "sgf";
        var result = Assert.IsType<MemberRegistrationResult>(Assert.IsType<OkObjectResult>((await f.Controller.Register(request)).Result).Value);
        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
        Assert.Equal(new[] { "Save", "AddToRolesAsync", "GetRolesAsync", "SignInAsync" }, f.Calls.SkipWhile(x => x != "Save"));
        Assert.True(f.Member.IsApproved);
        Assert.Equal("Member", f.Member.ContentType.Alias);
        Assert.Equal("Synthetic", f.Member.GetValue<string>("firstName"));
        Assert.Equal("Member", f.Member.GetValue<string>("lastName"));
        Assert.Equal("Synthetic", f.Member.GetValue<string>("username"));
    }

    [Fact]
    public async Task FailedGroupAssignmentReturnsUnavailableAndDeletesPartialAccount()
    {
        var f = new Fixture { RoleFailure = true };
        var result = Assert.IsType<ObjectResult>((await f.Controller.Register(Valid())).Result);
        Assert.Equal(503, result.StatusCode);
        Assert.False(Assert.IsType<MemberRegistrationResult>(result.Value).Succeeded);
        Assert.Contains("DeleteAsync", f.Calls);
        Assert.DoesNotContain("SignInAsync", f.Calls);
    }

    private sealed class Fixture
    {
        public string? Missing, Duplicate;
        public bool RoleFailure;
        public List<string> Calls { get; } = new();
        public Member Member { get; }
        public MemberRegistrationController Controller { get; }
        public Fixture()
        {
            var helper = new DefaultShortStringHelper(new DefaultShortStringHelperConfig());
            var type = new MemberType(helper, -1) { Alias = "Member" };
            foreach (var alias in new[] { "firstName", "lastName", "username" })
                type.AddPropertyType(new PropertyType(helper, "Umbraco.TextBox", ValueStorageType.Nvarchar, alias));
            Member = new Member("Synthetic Member", "synthetic@example.test", "Synthetic", type);
            var types = Proxy<IMemberTypeService>((method, _) => Missing == "type" ? null :
                Missing == "properties" ? new MemberType(helper, -1) { Alias = "Member" } : type);
            var groups = Proxy<IMemberGroupService>((_, _) => Missing == "group" ? null : new MemberGroup { Name = "SGF Devs" });
            var members = Proxy<IMemberService>((method, _) => method.Name switch
            {
                "GetByEmail" => Duplicate == "email" ? Member : null,
                "GetByUsername" => Duplicate == "username" ? Member : null,
                "GetById" => Member,
                "Save" => Save(),
                _ => throw new Exception(method.Name)
            });
            var manager = Proxy<IMemberManager>((method, args) =>
            {
                Calls.Add(method.Name);
                if (method.Name == "CreateAsync")
                {
                    var identity = Assert.IsType<MemberIdentityUser>(args![0]);
                    Assert.Equal("Synthetic", identity.UserName);
                    Assert.Equal("synthetic@example.test", identity.Email);
                    Assert.Equal("Synthetic Member", identity.Name);
                    return Task.FromResult(IdentityResult.Success);
                }
                if (method.Name == "AddToRolesAsync")
                {
                    Assert.Equal(new[] { "SGF Devs" }, Assert.IsAssignableFrom<IEnumerable<string>>(args![1]));
                    return Task.FromResult(RoleFailure ? IdentityResult.Failed(new IdentityError { Description = "private service detail" }) : IdentityResult.Success);
                }
                if (method.Name == "GetRolesAsync") return Task.FromResult<IList<string>>(new[] { "SGF Devs" });
                if (method.Name == "DeleteAsync") return Task.FromResult(IdentityResult.Success);
                throw new Exception(method.Name);
            });
            var signIn = Proxy<IMemberSignInManager>((method, args) =>
            {
                Calls.Add(method.Name);
                if (method.Name == "SignInAsync") Assert.Equal(true, args![1]);
                return Task.CompletedTask;
            });
            Controller = new(manager, signIn, members, types, groups, NullLogger<MemberRegistrationController>.Instance);
        }
        private Attempt<OperationResult?> Save()
        {
            Calls.Add("Save");
            return Attempt.Succeed<OperationResult?>(null);
        }
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, MemberLoginTests.InterfaceProxy>();
        ((MemberLoginTests.InterfaceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
