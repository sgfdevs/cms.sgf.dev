#nullable enable

using System;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Infrastructure.BackgroundJobs.Jobs;

namespace SgfDevs.Dev.LocalBootstrap;

public static class LocalBootstrapTelemetryGuard
{
    public static void RemoveTelemetryJob(IServiceCollection services, LocalBootstrapGuardResult guardResult)
    {
        if (!guardResult.BootstrapEnabled)
        {
            return;
        }

        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (IsReportSiteJobDescriptor(descriptor))
            {
                services.RemoveAt(index);
            }
        }
    }

    private static bool IsReportSiteJobDescriptor(ServiceDescriptor descriptor)
    {
        if (descriptor.ServiceType == typeof(ReportSiteJob) || descriptor.ImplementationType == typeof(ReportSiteJob))
        {
            return true;
        }

        return descriptor.ServiceType == typeof(IRecurringBackgroundJob) &&
            descriptor.ImplementationFactory is not null &&
            descriptor.ImplementationFactory.Method.ReturnType == typeof(ReportSiteJob);
    }
}
