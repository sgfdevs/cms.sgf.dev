#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace SgfDevs.Dev;

// This API is only for the frontend server. It is not a browser CORS endpoint.
public sealed class MemberBridge(RequestDelegate next, IConfiguration configuration)
{
    public const string Header = "X-SGF-Member-Bridge";
    public const string Prefix = "/api/v1/member";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(Prefix))
        {
            await next(context);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        var secret = configuration["SGFDevs:MemberBridge:Secret"];
        var supplied = context.Request.Headers[Header];
        if (string.IsNullOrWhiteSpace(secret) || supplied.Count != 1 ||
            context.Request.Headers.ContainsKey("Origin") ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(secret)),
                SHA256.HashData(Encoding.UTF8.GetBytes(supplied[0] ?? ""))))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }
}
