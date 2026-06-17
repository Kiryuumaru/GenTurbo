using Application.Jobs.Extensions;
using Application.Shared.Extensions;
using Application.Workers.Extensions;
using ApplicationBuilderHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Application;

public class Application : ApplicationDependency
{
    public override void AddServices(ApplicationHostBuilder applicationBuilder, IServiceCollection services)
    {
        base.AddServices(applicationBuilder, services);

        services.AddSharedServices();
        services.AddJobsServices();
        services.AddWorkersServices();
    }
}
