#nullable enable

using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SGFDevs.Controllers;
using SGFDevs.ViewModels;
using SgfDevs.Dev;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Security;
using Xunit;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace SgfDevs.Tests;

public class MemberLoginTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData("", "", false)]
    [InlineData("configured", null, false)]
    [InlineData("configured", "wrong", false)]
    [InlineData("configured", "configured", true)]
    public async Task BridgeFailsClosed(string? secret, string? supplied, bool allowed)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["SGFDevs:MemberBridge:Secret"] = secret }).Build();
        var invoked = false;
        var middleware = new MemberBridge(_ => { invoked = true; return Task.CompletedTask; }, config);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/member/session";
        if (supplied is not null) context.Request.Headers[MemberBridge.Header] = supplied;
        await middleware.InvokeAsync(context);
        Assert.Equal(allowed, invoked);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.Equal(allowed ? 200 : 401, context.Response.StatusCode);
        if (allowed)
        {
            invoked = false;
            context.Request.Headers.Origin = "https://browser.example";
            await middleware.InvokeAsync(context);
            Assert.False(invoked);
            Assert.Equal(401, context.Response.StatusCode);
            Assert.False(context.Response.Headers.ContainsKey("Access-Control-Allow-Origin"));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoginUsesUmbracoNormalizationRememberMeAndLockout(bool remember)
    {
        var signIn = Proxy<IMemberSignInManager>((method, args) =>
        {
            Assert.Equal("PasswordSignInAsync", method.Name);
            Assert.Equal(new object[] { "  Alice  ", "untouched password ", remember, true }, args);
            return Task.FromResult(SignInResult.Success);
        });
        var controller = new MemberSessionController(signIn, Proxy<IMemberManager>((_, _) => throw new Exception()));
        var result = await controller.Login(new("  Alice  ", "untouched password ", remember));
        Assert.True(Assert.IsType<MemberLoginResult>(Assert.IsType<OkObjectResult>(result.Result).Value).Succeeded);
        // Installed Umbraco 18.2 member sign-in uses ASP.NET Identity's member application scheme.
        var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MemberSignInManager));
        var scheme = typeof(MemberSignInManager).GetProperty("AuthenticationType", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.Equal(IdentityConstants.ApplicationScheme, scheme.GetValue(instance));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedAndLockedOutLoginsHaveSameGenericResult(bool locked)
    {
        var signIn = Proxy<IMemberSignInManager>((_, _) => Task.FromResult(locked ? SignInResult.LockedOut : SignInResult.Failed));
        var controller = new MemberSessionController(signIn, Proxy<IMemberManager>((_, _) => null));
        var result = await controller.Login(new("Alice", "incorrect"));
        Assert.Equal(new MemberLoginResult(false), Assert.IsType<OkObjectResult>(result.Result).Value);
    }

    [Fact]
    public async Task SessionAndLogoutAuthenticateOnlyMemberSchemeAndExposeOnlyMinimalIdentity()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Alice") }, IdentityConstants.ApplicationScheme));
        var authenticated = true;
        var auth = Proxy<IAuthenticationService>((method, args) =>
        {
            Assert.Equal("AuthenticateAsync", method.Name);
            Assert.Equal(IdentityConstants.ApplicationScheme, args![1]);
            return Task.FromResult(authenticated
                ? AuthenticateResult.Success(new AuthenticationTicket(principal, IdentityConstants.ApplicationScheme))
                : AuthenticateResult.NoResult());
        });
        var signedOut = false;
        var signIn = Proxy<IMemberSignInManager>((method, _) =>
        {
            Assert.Equal("SignOutAsync", method.Name);
            signedOut = true;
            return Task.CompletedTask;
        });
        var member = Proxy<IMemberManager>((method, _) =>
        {
            Assert.Equal("GetCurrentMemberAsync", method.Name);
            return Task.FromResult(new MemberIdentityUser { UserName = "Alice", Name = "Alice Example", Email = "private@example.test" });
        });
        using var services = new ServiceCollection().AddSingleton(auth).BuildServiceProvider();
        var controller = new MemberSessionController(signIn, member)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } } };
        var session = await controller.Session();
        Assert.Equal(new MemberSessionDto("Alice", "Alice Example"), Assert.IsType<OkObjectResult>(session.Result).Value);
        Assert.Equal(new[] { "Name", "Username" }, typeof(MemberSessionDto).GetProperties().Select(p => p.Name).Order());
        Assert.Same(principal, controller.User);
        authenticated = false;
        Assert.IsType<UnauthorizedResult>((await controller.Session()).Result);
        Assert.IsType<NoContentResult>(await controller.Logout());
        Assert.True(signedOut);
        Assert.False(controller.User.Identity?.IsAuthenticated ?? false);
        Assert.IsType<HttpPostAttribute>(typeof(MemberSessionController).GetMethod("Logout")!.GetCustomAttribute<HttpPostAttribute>());
    }

    public class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
