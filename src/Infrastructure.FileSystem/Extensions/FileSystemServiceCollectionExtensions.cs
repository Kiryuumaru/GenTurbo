using Application.Workers.Interfaces.Outbound;
using Infrastructure.FileSystem.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.FileSystem.Extensions;

internal static class FileSystemServiceCollectionExtensions
{
    internal static IServiceCollection AddFileSystemServices(this IServiceCollection services)
    {
        services.AddScoped<IFileStorage, FileSystemStorageAdapter>();
        return services;
    }
}
