using ApplicationBuilderHelpers;
using Infrastructure.Python.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Python;

public class PythonInfrastructure : ApplicationDependency
{
    public override void AddServices(ApplicationHostBuilder applicationBuilder, IServiceCollection services)
    {
        base.AddServices(applicationBuilder, services);

        services.AddPythonServices();
    }
}
