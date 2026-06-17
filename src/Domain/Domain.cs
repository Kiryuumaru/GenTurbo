using Domain.Jobs.Extensions;
using Domain.Shared.Extensions;
using Domain.Workers.Extensions;
using ApplicationBuilderHelpers;
using Microsoft.Extensions.DependencyInjection;

namespace Domain;

public class Domain : ApplicationDependency
{
    public override void AddServices(ApplicationHostBuilder applicationBuilder, IServiceCollection services)
    {
        base.AddServices(applicationBuilder, services);

        services.AddSharedServices();
        services.AddJobsServices();
        services.AddWorkersServices();
    }
}
