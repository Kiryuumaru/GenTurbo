using ApplicationBuilderHelpers;
using ApplicationBuilderHelpers.Attributes;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Presentation.Server.Endpoints;

namespace Presentation.Server.Commands;

[Command("GenTurbo orchestrator — REST API, job queue, worker registry.")]
internal class MainCommand : Build.BaseCommand<WebApplicationBuilder>
{
    [CommandOption("port", Description = "Port to listen on.")]
    public int Port { get; set; } = 7860;

    private WebApplicationBuilder _webAppBuilder = null!;

    protected override ValueTask<WebApplicationBuilder> ApplicationBuilder(CancellationToken stoppingToken)
    {
        _webAppBuilder = WebApplication.CreateBuilder();
        _webAppBuilder.WebHost.UseUrls($"http://0.0.0.0:{Port}");
        return new ValueTask<WebApplicationBuilder>(_webAppBuilder);
    }

    public override void AddServices(ApplicationHostBuilder applicationBuilder, IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new()
                {
                    Title = "gen-turbo",
                    Version = "1.0.0",
                    Summary = "Distributed multi-model media generation platform",
                    Description = """
gen-turbo is a distributed job queue for generative AI models (images, video, audio, 3D).

## Architecture

- **Orchestrator** (this server) — Job queue, worker registry, file serving, REST API
- **Workers** — GPU nodes that poll for jobs, run inference, and upload results

## Quickstart

1. List available models: `GET /models`
2. Check model params: `GET /models/{model_id}`
3. Submit a job: `POST /generate`
4. Poll for completion: `GET /jobs/{request_id}`
5. Download result: `GET /files/{filename}`

## Authentication

All `/worker/*` endpoints are internal-only (Tailscale). Client endpoints are public.
""",
                    Contact = new() { Name = "gen-turbo" }
                };
                return Task.CompletedTask;
            });
        });

        base.AddServices(applicationBuilder, services);
    }

    protected override async ValueTask Run(ApplicationHost<WebApplicationBuilder> applicationHost, CancellationTokenSource cancellationTokenSource)
    {
        await base.Run(applicationHost, cancellationTokenSource);

        var app = _webAppBuilder.Build();

        app.MapOpenApi();

        ClientEndpoints.Map(app);
        WorkerEndpoints.Map(app);

        await app.RunAsync();
    }
}
