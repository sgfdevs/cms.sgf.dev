#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace SgfDevs.Dev;

public class NewsletterHelper
{
    public const string ClientName = "newsletter";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _endpoint;
    private readonly string? _listId;

    public NewsletterHelper(IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
        _endpoint = configuration["SGFDevs:NewsletterEndpoint"];
        _listId = configuration["SGFDevs:NewsletterListId"];
    }

    // Trusted configuration may point to an internal HTTP service.
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_listId) &&
        Uri.TryCreate(_endpoint, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);

    public async Task<bool> Subscribe(string email, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable) return false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = JsonContent.Create(new Dictionary<string, object>
                {
                    { "email", email },
                    { "list_uuids", new[] { _listId! } }
                })
            };
            var client = _httpClientFactory.CreateClient(ClientName);
            using var response = await client.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) return false;
            using var jsonStream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var result = await JsonSerializer.DeserializeAsync<SubscribeResponseDto>(
                jsonStream, new JsonSerializerOptions(JsonSerializerDefaults.Web), timeout.Token);
            return result?.Data == true;
        }
        catch
        {
            // Provider exceptions can contain configured URLs or submitted addresses.
            return false;
        }
    }

    public class SubscribeResponseDto
    {
        public bool Data { get; set; }
    }
}
