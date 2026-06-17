using Microsoft.Extensions.DependencyInjection;

namespace Presentation.Server;

internal static class GenTurboServiceExtensions
{
    internal static IServiceCollection AddGenTurbo(this IServiceCollection services)
    {
        Domain.Shared.Extensions.SharedServiceCollectionExtensions.AddSharedServices(services);
        Domain.AppEnvironment.Extensions.AppEnvironmentServiceCollectionExtensions.AddAppEnvironmentServices(services);
        Domain.Jobs.Extensions.JobsServiceCollectionExtensions.AddJobsServices(services);
        Domain.Workers.Extensions.WorkersServiceCollectionExtensions.AddWorkersServices(services);
        Application.Shared.Extensions.SharedServiceCollectionExtensions.AddSharedServices(services);
        Application.EmbeddedConfig.Extensions.EmbeddedConfigServiceCollectionExtensions.AddEmbeddedConfigServices(services);
        Application.Jobs.Extensions.JobsServiceCollectionExtensions.AddJobsServices(services);
        Application.Workers.Extensions.WorkersServiceCollectionExtensions.AddWorkersServices(services);
        Infrastructure.Sqlite.Extensions.SqliteServiceCollectionExtensions.AddSqliteServices(services);
        Infrastructure.Python.ZImage.Extensions.ZImageServiceCollectionExtensions.AddZImageServices(services);
        Infrastructure.FileSystem.Extensions.FileSystemServiceCollectionExtensions.AddFileSystemServices(services);
        return services;
    }
}
