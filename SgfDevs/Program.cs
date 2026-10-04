using System;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Data.Sqlite;
using Microsoft.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SGFDevs.Controllers;
using SgfDevs.Dev;
using SgfDevs.Dev.EventSync;
using SgfDevs.Dev.EventSync.Meetup;
using SgfDevs.Dev.EventSync.Sessionize;
using SgfDevs.Dev.LocalBootstrap;
using SgfDevs.HealthChecks;
using SGFDevs.Dev;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Api.Common.DependencyInjection;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.DependencyInjection;
using Umbraco.Cms.Persistence.Sqlite;
using Umbraco.Extensions;

var builder = WebApplication.CreateBuilder(args);

LocalSchemaBootstrapGuard.RejectUnsupportedConfigPath(builder.Configuration, builder.Environment);
var localBootstrapConfigLoaded = LocalBootstrapConfigurationLoader.AddLocalBootstrapConfiguration(builder.Configuration, builder.Environment);
LocalSchemaBootstrapGuard.RejectUnsupportedEffectiveRequest(builder.Configuration, builder.Environment);
var localBootstrapGuardResult = LocalBootstrapGuard.ValidateStartupConfiguration(builder.Configuration, builder.Environment);
LocalBootstrapEffectivePolicyValidator.Validate(builder.Configuration, localBootstrapGuardResult, localBootstrapConfigLoaded);

builder.WebHost.UseSentry();

var umbracoBuilder = builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddDeliveryApi()
    .AddComposers();
LocalBootstrapTelemetryGuard.RemoveTelemetryJob(builder.Services, localBootstrapGuardResult);

if (!string.IsNullOrEmpty(builder.Configuration["Umbraco:Storage:Cdn:Url"]))
{
    umbracoBuilder.AddCdnMediaUrlProvider();
}

var serverRoleName = builder.Configuration["SGFDevs:ServerRole"] ?? nameof(ServerRole.Single);
if (!Enum.TryParse(serverRoleName, false, out ServerRole serverRole) ||
    serverRole is ServerRole.Unknown ||
    !Enum.IsDefined(serverRole))
{
    throw new InvalidOperationException(
        $"Unsupported SGFDevs:ServerRole value '{serverRoleName}'. " +
        $"Expected {nameof(ServerRole.Single)}, {nameof(ServerRole.SchedulingPublisher)}, or {nameof(ServerRole.Subscriber)}.");
}

umbracoBuilder.SetServerRegistrar(new FixedServerRoleAccessor(serverRole));
umbracoBuilder.Build();

builder.Services.AddOpenApi("sgf-public-v1", options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "SGF public API",
            Version = "1.0",
            Description = "Typed public endpoints for SGF frontend clients."
        };
        return Task.CompletedTask;
    });
    options.ShouldInclude = apiDescription =>
    {
        if (apiDescription.ActionDescriptor is not ControllerActionDescriptor actionDescriptor)
        {
            return false;
        }

        return actionDescriptor.AttributeRouteInfo?.Name is
            "Directory_GetSkillNames" or
            "Directory_GetSkillFilters" or
            "Directory_Search" or
            "PublicHome_Get";
    };
});
builder.Services.AddOpenApiDocumentToUi("sgf-public-v1", "SGF public API v1");

builder.Services.AddHealthChecks()
    .AddCheck<ReadinessHealthCheck>("ready", tags: ["ready"]);
builder.Services.AddHttpClient();
builder.Services.Configure<EventSyncOptions>(builder.Configuration.GetSection("SGFDevs"));
builder.Services.Configure<SiteFeaturesOptions>(builder.Configuration.GetSection("SGFDevs:Site"));
builder.Services.AddScoped<MemberConverter>();
builder.Services.AddScoped<MemberTagDisplayService>();
builder.Services.AddScoped<PresentationPresenterDisplayService>();
builder.Services.AddScoped(_ => new EventDisplayService(EventSyncTimeZoneResolver.Resolve(builder.Configuration["SGFDevs:EventTimeZoneId"])));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IPublicContentProtectionLookup, PublicContentProtectionLookup>();
builder.Services.AddScoped<PublicContentAccessGuard>();
builder.Services.AddScoped<PublicHomeBuilder>();
builder.Services.AddScoped<PublicHomeService>();
builder.Services.AddScoped<DirectoryHelper>();
builder.Services.AddScoped<NewsletterHelper>();
builder.Services.AddScoped<EventSyncImportFilter>();
builder.Services.AddScoped<PresenterMemberMatcher>();
builder.Services.AddScoped<SessionizeSyncPlanner>();
builder.Services.AddScoped<MeetupEventMatcher>();
builder.Services.AddScoped<ImportedPresenterBlockBuilder>();
builder.Services.AddScoped<SessionizeApiClient>();
builder.Services.AddScoped<MeetupApiClient>();
builder.Services.AddScoped<SessionizeSpeakerMediaService>();
builder.Services.AddScoped<SessionizeEventSyncService>();

var app = builder.Build();

if (builder.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

await app.BootUmbracoAsync();

var healthCheckOptions = new HealthCheckOptions
{
    ResponseWriter = static (_, _) => Task.CompletedTask
};
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = static _ => false,
    ResponseWriter = healthCheckOptions.ResponseWriter
}).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = static check => check.Tags.Contains("ready"),
    ResponseWriter = healthCheckOptions.ResponseWriter
}).AllowAnonymous();
app.MapGet("/robots.txt", (IOptions<SiteFeaturesOptions> siteFeatures) =>
    Results.Text(
        siteFeatures.Value.SearchIndexingEnabled
            ? "User-agent: *\nAllow: /\n"
            : "User-agent: *\nDisallow: /\n",
        "text/plain"
    )
).AllowAnonymous();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.EndpointRouteBuilder.MapControllerRoute(
            "ProfileCustomRoute",
            "member/{username:regex(^[a-zA-Z0-9]+$)}",
            new { Controller = "Member", Action = "MemberProfile" });
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
