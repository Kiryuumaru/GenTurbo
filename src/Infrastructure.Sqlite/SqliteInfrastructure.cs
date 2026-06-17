using ApplicationBuilderHelpers;
using Infrastructure.Sqlite.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Sqlite;

public class SqliteInfrastructure : ApplicationDependency
{
    public override void AddServices(ApplicationHostBuilder applicationBuilder, IServiceCollection services)
    {
        base.AddServices(applicationBuilder, services);

        services.AddSqliteServices();
    }
}
