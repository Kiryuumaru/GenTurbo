using Presentation.Server;
using Presentation.Server.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

var portArg = args.FirstOrDefault(a => a.StartsWith("--port="))?.Split('=')[1] ?? "7860";
builder.WebHost.UseUrls($"http://0.0.0.0:{portArg}");

builder.Services.Configure<ConsoleLifetimeOptions>(opts => opts.SuppressStatusMessages = true);
builder.Services.AddLogging(b => { b.SetMinimumLevel(LogLevel.Information); b.AddConsole(); });
builder.Services.AddGenTurbo();

builder.Services.AddOpenApi(options =>
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

var app = builder.Build();

app.MapOpenApi();

ClientEndpoints.Map(app);
WorkerEndpoints.Map(app);

app.Run();
