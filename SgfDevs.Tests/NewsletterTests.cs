#nullable enable

using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using SGFDevs.Controllers;
using SgfDevs.Dev;
using Xunit;

namespace SgfDevs.Tests;

public class NewsletterTests
{
    [Theory]
    [InlineData("bad", "", true, 0, 400)]
    [InlineData("person@example.test", " ", true, 0, 400)]
    [InlineData("person@example.test", "bot", true, 0, 400)]
    [InlineData("person@example.test", "", false, 0, 503)]
    [InlineData("person@example.test", "", true, 1, 200)]
    public async Task SignupValidatesBeforeAnyProviderRequest(
        string email, string name, bool enabled, int calls, int status)
    {
        using var handler = new Provider(HttpStatusCode.OK, "{\"data\":true}");
        var controller = Controller(handler, enabled ? "http://trusted-internal-service/subscribe" : "", enabled ? "configured-list" : "");
        var result = await controller.Signup(new(email, name));
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result.Result).StatusCode);
        Assert.Equal(calls, handler.Calls);
        Assert.Equal(status == 200, Assert.IsType<NewsletterSignupResult>(
            Assert.IsAssignableFrom<ObjectResult>(result.Result).Value).Accepted);
    }

    [Theory]
    [InlineData(200, "{\"data\":false}")]
    [InlineData(503, "{\"data\":true}")]
    [InlineData(200, "not json")]
    public async Task FailedProviderRequestIsNotAccepted(int status, string body)
    {
        using var handler = new Provider((HttpStatusCode)status, body);
        var result = await Controller(handler).Signup(new("person@example.test", ""));
        Assert.Equal(503, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task MissingListAndProviderExceptionFailSafely()
    {
        using var handler = new Provider(HttpStatusCode.OK, "{\"data\":true}");
        var disabled = await Controller(handler, list: "").Signup(new("person@example.test", ""));
        Assert.Equal(503, Assert.IsType<ObjectResult>(disabled.Result).StatusCode);
        Assert.Equal(0, handler.Calls);
        handler.Throw = true;
        var failed = await Controller(handler).Signup(new("person@example.test", ""));
        Assert.Equal(503, Assert.IsType<ObjectResult>(failed.Result).StatusCode);
        Assert.DoesNotContain("private", Assert.IsType<NewsletterSignupResult>(
            Assert.IsType<ObjectResult>(failed.Result).Value).Error!);
    }

    private static NewsletterController Controller(Provider handler,
        string endpoint = "http://trusted-internal-service/subscribe", string list = "configured-list")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["SGFDevs:NewsletterEndpoint"] = endpoint, ["SGFDevs:NewsletterListId"] = list }).Build();
        return new NewsletterController(new NewsletterHelper(config, new Factory(handler)));
    }

    private sealed class Factory(Provider handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(NewsletterHelper.ClientName, name);
            return new HttpClient(handler, false);
        }
    }

    private sealed class Provider(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls;
        public bool Throw;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Empty(request.Headers);
            Assert.Equal("{\"email\":\"person@example.test\",\"list_uuids\":[\"configured-list\"]}",
                await request.Content!.ReadAsStringAsync(token));
            if (Throw) throw new HttpRequestException("private endpoint and credentials");
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
