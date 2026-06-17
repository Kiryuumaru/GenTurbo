using Microsoft.Extensions.DependencyInjection;

namespace Presentation.Api;

internal static class GenTurboServiceExtensions
{
    internal static IServiceCollection AddGenTurbo(this IServiceCollection services)
    {
        Domain.Shared.Extensions.SharedServiceCollectionExtensions.AddSharedServices(services);
        Domain.Jobs.Extensions.JobsServiceCollectionExtensions.AddJobsServices(services);
        Domain.Workers.Extensions.WorkersServiceCollectionExtensions.AddWorkersServices(services);
        Application.Shared.Extensions.SharedServiceCollectionExtensions.AddSharedServices(services);
        Application.Jobs.Extensions.JobsServiceCollectionExtensions.AddJobsServices(services);
        Application.Workers.Extensions.WorkersServiceCollectionExtensions.AddWorkersServices(services);
        Infrastructure.Sqlite.Extensions.SqliteServiceCollectionExtensions.AddSqliteServices(services);
        Infrastructure.Python.Extensions.PythonServiceCollectionExtensions.AddPythonServices(services);
        Infrastructure.FileSystem.Extensions.FileSystemServiceCollectionExtensions.AddFileSystemServices(services);
        return services;
    }
}
