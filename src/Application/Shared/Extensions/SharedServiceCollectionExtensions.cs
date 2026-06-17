using Application.Shared.Interfaces;
using Application.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Application.Shared.Extensions;

public static class SharedServiceCollectionExtensions
{
    internal static IServiceCollection AddSharedServices(this IServiceCollection services)
    {
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        return services;
    }

    public static IServiceCollection AddDomainEventHandler<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this IServiceCollection services)
        where THandler : class, IDomainEventHandler
    {
        services.AddScoped<IDomainEventHandler, THandler>();
        return services;
    }
}
