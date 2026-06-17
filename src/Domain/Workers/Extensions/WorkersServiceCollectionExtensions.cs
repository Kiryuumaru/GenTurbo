using Microsoft.Extensions.DependencyInjection;

namespace Domain.Workers.Extensions;

internal static class WorkersServiceCollectionExtensions
{
    internal static IServiceCollection AddWorkersServices(this IServiceCollection services)
    {
        return services;
    }
}
