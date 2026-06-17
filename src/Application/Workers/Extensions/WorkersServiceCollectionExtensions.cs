using Application.Workers.Interfaces.Inbound;
using Application.Workers.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Workers.Extensions;

internal static class WorkersServiceCollectionExtensions
{
    internal static IServiceCollection AddWorkersServices(this IServiceCollection services)
    {
        services.AddScoped<IWorkerService, WorkerService>();
        services.AddScoped<IWorkerInferenceService, WorkerInferenceService>();
        return services;
    }
}
