using Microsoft.Extensions.DependencyInjection;

namespace Domain.Jobs.Extensions;

internal static class JobsServiceCollectionExtensions
{
    internal static IServiceCollection AddJobsServices(this IServiceCollection services)
    {
        return services;
    }
}
