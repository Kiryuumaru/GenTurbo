using ApplicationBuilderHelpers;
using Infrastructure.FileSystem.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.FileSystem;

public class FileSystemInfrastructure : ApplicationDependency
{
    public override void AddServices(ApplicationHostBuilder applicationBuilder, IServiceCollection services)
    {
        base.AddServices(applicationBuilder, services);

        services.AddFileSystemServices();
    }
}
