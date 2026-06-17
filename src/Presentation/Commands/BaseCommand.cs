using Application.Shared.Interfaces.Inbound;
using Application.Logger.Extensions;
using ApplicationBuilderHelpers;
using ApplicationBuilderHelpers.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Presentation.Commands;

public abstract class BaseCommand<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] THostApplicationBuilder> : Command<THostApplicationBuilder>
    where THostApplicationBuilder : IHostApplicationBuilder
{
    [CommandOption(
        'l', "log-level",
        EnvironmentVariable = "LOG_LEVEL",
        Description = "Level of logs to show.")]
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    public abstract IApplicationConstants ApplicationConstants { get; }

    public override void AddConfigurations(ApplicationHostBuilder applicationBuilder, IConfiguration configuration)
    {
        base.AddConfigurations(applicationBuilder, configuration);

        configuration.LoggerLevel = LogLevel;
    }

    public override void AddServices(ApplicationHostBuilder applicationBuilder, IServiceCollection services)
    {
        services.Configure<ConsoleLifetimeOptions>(opts => opts.SuppressStatusMessages = true);

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(applicationBuilder.Configuration.LoggerLevel);
            builder.AddConsole();
        });

        base.AddServices(applicationBuilder, services);
    }

    protected override async ValueTask Run(ApplicationHost<THostApplicationBuilder> applicationHost, CancellationTokenSource cancellationTokenSource)
    {
        using var scope = applicationHost.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<BaseCommand<THostApplicationBuilder>>>();

        Console.WriteLine();
        Console.WriteLine("  GenTurbo — Distributed Multi-Model Media Generation Platform");
        Console.WriteLine();

        logger.LogInformation("GenTurbo orchestrator starting");
    }
}
