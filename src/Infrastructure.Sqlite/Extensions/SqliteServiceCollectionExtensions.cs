using Domain.Jobs.Interfaces;
using Domain.Workers.Interfaces;
using Infrastructure.Sqlite.Adapters;
using Infrastructure.Sqlite.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Sqlite.Extensions;

internal static class SqliteServiceCollectionExtensions
{
    internal static IServiceCollection AddSqliteServices(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var connection = new SqliteConnection("Data Source=/app/data/genturbo.db");
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL";
            cmd.ExecuteNonQuery();
            cmd.CommandText = "PRAGMA foreign_keys=ON";
            cmd.ExecuteNonQuery();
            return connection;
        });

        services.AddScoped<SqliteUnitOfWork>();
        services.AddScoped<SqliteJobRepository>();
        services.AddScoped<SqliteWorkerRepository>();

        services.AddScoped<IJobRepository>(sp => sp.GetRequiredService<SqliteJobRepository>());
        services.AddScoped<IJobUnitOfWork, SqliteJobUnitOfWork>();
        services.AddScoped<IWorkerRepository>(sp => sp.GetRequiredService<SqliteWorkerRepository>());
        services.AddScoped<IWorkerUnitOfWork, SqliteWorkerUnitOfWork>();

        return services;
    }
}
