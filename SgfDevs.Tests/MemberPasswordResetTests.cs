#nullable enable

using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SGFDevs.Controllers;
using SGFDevs.Models;
using SGFDevs.ViewModels;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models.Email;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Security;
using Xunit;

namespace SgfDevs.Tests;

public class MemberPasswordResetTests
{
    [Fact]
    public async Task ForgotIsGenericIncludingSendFailureAndEncodesCanonicalLinkAndName()
    {
        var f = new Fixture();
        var found = await f.Controller.ForgotPassword(new() { Email = "fictional@example.test" });
        var message = Assert.IsType<EmailMessage>(f.Message);
        var body = Assert.IsType<string>(message.Body);
        Assert.DoesNotContain("<script>", body);
        Assert.Contains("&lt;script&gt;", body);
        var link = WebUtility.HtmlDecode(Regex.Match(body, "href=\"([^\"]+)\"").Groups[1].Value);
        var url = new Uri(link);
        Assert.Equal("https://frontend.example.test/reset-password", url.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(url.Query);
        Assert.Equal(f.Member.Id, query["memberId"]);
        Assert.Equal(Fixture.Token, query["token"]);
        Assert.Equal(f.Expiration, f.SentExpiration);
        f.Exists = false;
        var absent = await f.Controller.ForgotPassword(new() { Email = "absent@example.test" });
        Assert.Equal(Result(found).Succeeded, Result(absent).Succeeded);
        Assert.Empty(Result(found).Errors);
        f.Exists = true;
        f.SendFailure = true;
        Assert.True(Result(await f.Controller.ForgotPassword(new() { Email = "fictional@example.test" })).Succeeded);
    }

    [Theory]
    [InlineData("", true, "sender@example.test")]
    [InlineData("http://frontend.example.test", true, "sender@example.test")]
    [InlineData("https://frontend.example.test/path", true, "sender@example.test")]
    [InlineData("https://frontend.example.test", false, "sender@example.test")]
    [InlineData("https://frontend.example.test", true, "")]
    public async Task UnavailableBeforeAccountLookup(string origin, bool ready, string from)
    {
        var f = new Fixture(origin, ready, from);
        var response = Assert.IsType<ObjectResult>((await f.Controller.ForgotPassword(new() { Email = "fictional@example.test" })).Result);
        Assert.Equal(503, response.StatusCode);
        Assert.Empty(f.Calls);
        Assert.Null(f.Message);
    }

    [Fact]
    public async Task MissingTokenAndConfirmationMismatchNeverReachIdentity()
    {
        var f = new Fixture();
        var request = Valid();
        request.Token = "";
        Assert.Equal("This reset link is invalid or has expired.", Assert.Single(Result(await f.Controller.ResetPassword(request)).Errors[""]));
        request = Valid();
        request.ConfirmPassword = "Different9!";
        Assert.Equal("Passwords do not match.", Assert.Single(Result(await f.Controller.ResetPassword(request)).Errors["confirmPassword"]));
        Assert.Empty(f.Calls);
    }

    [Theory]
    [InlineData("InvalidToken", "")]
    [InlineData("PasswordTooShort", "password")]
    [InlineData("UnknownPrivateError", "")]
    public async Task MapsIdentityErrorsWithoutDescriptions(string code, string field)
    {
        var f = new Fixture { ResetResult = IdentityResult.Failed(new IdentityError { Code = code, Description = "private token/password/member detail" }) };
        var result = Result(await f.Controller.ResetPassword(Valid()));
        Assert.False(result.Succeeded);
        Assert.Equal(field == "password" ? PasswordValidationRules.ErrorMessage : "This reset link is invalid or has expired.", Assert.Single(result.Errors[field]));
        Assert.DoesNotContain("private", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task SuccessfulResetPreservesOpaqueValuesAndDoesNotSignIn()
    {
        var f = new Fixture();
        Assert.True(Result(await f.Controller.ResetPassword(Valid())).Succeeded);
        Assert.Equal(new[] { "FindByIdAsync", "ResetPasswordAsync" }, f.Calls);
    }

    private static MemberResetPasswordRequest Valid() => new()
    {
        MemberId = "opaque/id+ =", Token = Fixture.Token, Password = "Fictional9!", ConfirmPassword = "Fictional9!"
    };
    private static MemberPasswordResetResult Result(ActionResult<MemberPasswordResetResult> result) =>
        Assert.IsType<MemberPasswordResetResult>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private sealed class Fixture
    {
        public const string Token = " +/opaque&=?%token= ";
        public bool Exists = true, SendFailure;
        public IdentityResult ResetResult = IdentityResult.Success;
        public List<string> Calls = new();
        public EmailMessage? Message;
        public TimeSpan Expiration = TimeSpan.FromHours(1);
        public object? SentExpiration;
        public MemberIdentityUser Member = MemberIdentityUser.CreateNew("Fictional", "fictional@example.test", "Member", true, "<script>");
        public MemberPasswordResetController Controller;
        public Fixture(string origin = "https://frontend.example.test", bool ready = true, string from = "sender@example.test")
        {
            Member.Id = "opaque/id+ =";
            var manager = Proxy<IMemberManager>((method, args) =>
            {
                Calls.Add(method.Name);
                return method.Name switch
                {
                    "FindByEmailAsync" => Task.FromResult(Exists ? Member : null),
                    "GeneratePasswordResetTokenAsync" => Task.FromResult(Token),
                    "FindByIdAsync" => Find(args),
                    "ResetPasswordAsync" => Reset(args),
                    _ => throw new Exception("Unexpected identity operation")
                };
            });
            var mail = Proxy<IEmailSender>((method, args) =>
            {
                if (method.Name == "CanSendRequiredEmail") return ready;
                Message = Assert.IsType<EmailMessage>(args![0]);
                Assert.Equal("PasswordReset", args[1]);
                Assert.Equal(true, args[2]);
                SentExpiration = args[3];
                if (SendFailure) throw new Exception("private mail details");
                return Task.CompletedTask;
            });
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SGFDevs:MemberBridge:FrontendOrigin"] = origin
            }).Build();
            Controller = new(manager, mail, Options.Create(new GlobalSettings { Smtp = new SmtpSettings { From = from, EmailExpiration = Expiration } }),
                config, new Host(), NullLogger<MemberPasswordResetController>.Instance);
        }
        private Task<MemberIdentityUser?> Find(object?[]? args)
        {
            Assert.Equal("opaque/id+ =", args![0]);
            return Task.FromResult<MemberIdentityUser?>(Member);
        }
        private Task<IdentityResult> Reset(object?[]? args)
        {
            Assert.Equal(Token, args![1]);
            Assert.Equal("Fictional9!", args[2]);
            return Task.FromResult(ResetResult);
        }
    }
    private sealed class Host : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Synthetic";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, MemberLoginTests.InterfaceProxy>();
        ((MemberLoginTests.InterfaceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
