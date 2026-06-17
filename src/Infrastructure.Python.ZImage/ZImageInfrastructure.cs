using ApplicationBuilderHelpers;
using Infrastructure.Python.ZImage.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Python.ZImage;

public class ZImageInfrastructure : ApplicationDependency
{
    public override void AddServices(ApplicationHostBuilder applicationBuilder, IServiceCollection services)
    {
        base.AddServices(applicationBuilder, services);

        services.AddZImageServices();
    }
}
