using Application.Jobs.Interfaces;
using Application.Jobs.Interfaces.Inbound;
using Application.Jobs.Services;
using Application.Jobs.Workers;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Jobs.Extensions;

internal static class JobsServiceCollectionExtensions
{
    internal static IServiceCollection AddJobsServices(this IServiceCollection services)
    {
        services.AddScoped<JobService>();
        services.AddScoped<IJobService>(sp => sp.GetRequiredService<JobService>());
        services.AddScoped<IJobOrchestrator>(sp => sp.GetRequiredService<JobService>());
        services.AddHostedService<TtlSweepWorker>();

        return services;
    }
}
