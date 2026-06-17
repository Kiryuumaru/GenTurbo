using ApplicationBuilderHelpers;
using ApplicationBuilderHelpers.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Presentation.Worker.Commands;

[Command("GenTurbo distributed media generation platform.")]
internal class MainCommand : Build.BaseCommand<HostApplicationBuilder>
{
    protected override ValueTask<HostApplicationBuilder> ApplicationBuilder(CancellationToken stoppingToken)
        => new(Host.CreateApplicationBuilder());

    protected override async ValueTask Run(ApplicationHost<HostApplicationBuilder> applicationHost, CancellationTokenSource cancellationTokenSource)
    {
        await base.Run(applicationHost, cancellationTokenSource);
        Console.WriteLine();
        Console.WriteLine("  GenTurbo - Distributed Multi-Model Media Generation Platform");
        Console.WriteLine();
        Console.WriteLine("  Components:");
        Console.WriteLine("    server    src/Presentation.Server  - Orchestrator REST API");
        Console.WriteLine("    worker    src/Presentation.Worker  - GPU inference node");
        Console.WriteLine();
        Console.WriteLine("  Run:");
        Console.WriteLine("    dotnet run --project src/Presentation.Server");
        Console.WriteLine("    dotnet run --project src/Presentation.Worker WorkerCommand -u http://server:7860 -i gpu-01");
        cancellationTokenSource.Cancel();
    }
}
