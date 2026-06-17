using Application.Jobs.Interfaces;
using Application.Jobs.Models;
using Application.Workers.Interfaces.Inbound;
using Application.Workers.Interfaces.Outbound;
using Domain.Jobs.Enums;
using Domain.Workers.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Presentation.Api.Endpoints;

internal static class WorkerEndpoints
{
    public static void Map(WebApplication app)
    {
        var worker = app.MapGroup("/worker").WithTags("worker");

        worker.MapPost("/register", async (
            [FromBody] JsonElement body,
            HttpContext httpContext,
            IWorkerService workerService,
            CancellationToken ct) =>
        {
            var workerId = body.GetProperty("worker_id").GetString()!;
            var name = body.GetProperty("name").GetString()!;
            var host = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            var models = new List<ModelCapability>();
            foreach (var m in body.GetProperty("models").EnumerateArray())
            {
                var modelId = m.GetProperty("id").GetString()!;
                var type = Enum.Parse<MediaType>(m.GetProperty("type").GetString()!, ignoreCase: true);
                var vram = m.TryGetProperty("vram_required_gb", out var v) ? v.GetInt32() : 0;
                var schema = m.TryGetProperty("param_schema", out var s)
                    ? JsonSerializer.Deserialize<Dictionary<string, object?>>(s.GetRawText()) ?? []
                    : new Dictionary<string, object?>();
                models.Add(ModelCapability.Create(modelId, type, vram, schema.AsReadOnly()));
            }

            await workerService.RegisterWorkerAsync(workerId, name, host, models, ct);
            return Results.Ok(new { status = "registered", worker_id = workerId });
        }).WithOpenApi(op => { op.Summary = "Register a worker"; op.Description = "Called by workers at startup. Announces capabilities. Idempotent."; return op; });

        worker.MapPost("/poll", async (
            [FromBody] JsonElement body,
            IWorkerService workerService,
            CancellationToken ct) =>
        {
            await workerService.MarkStaleWorkersAsync(ct);

            var workerId = body.GetProperty("worker_id").GetString()!;
            var job = await workerService.PollForJobAsync(workerId, ct);
            return Results.Ok(new { job });
        }).WithOpenApi(op => { op.Summary = "Poll for next job"; op.Description = "Called by workers in a loop. Returns next pending job or null."; return op; });

        worker.MapPost("/complete", async (
            [FromForm] string workerId,
            [FromForm] string jobId,
            [FromForm] string? error,
            [FromForm] string? errorType,
            IFormFile? file,
            IJobOrchestrator jobOrchestrator,
            IFileStorage fileStorage,
            ILogger<OrchestratorApi> logger,
            CancellationToken ct) =>
        {
            var jobGuid = Guid.Parse(jobId);

            if (error is not null)
            {
                await jobOrchestrator.FailJobAsync(jobGuid, workerId, error, errorType ?? "UNKNOWN", ct);
                return Results.Ok(new { status = "accepted" });
            }

            if (file is null)
                return Results.Problem("File required for successful completion", statusCode: 400);

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var url = fileStorage.Save(file.FileName, ms.ToArray());

            await jobOrchestrator.CompleteJobAsync(jobGuid, workerId, url, 0, ct);
            logger.LogInformation("Worker {WorkerId} completed job {JobId}", workerId, jobId);
            return Results.Ok(new { status = "accepted" });
        }).DisableAntiforgery()
          .WithOpenApi(op => { op.Summary = "Report job completion"; op.Description = "Multipart: upload generated file or report error."; return op; });

        worker.MapPost("/heartbeat", async (
            [FromBody] JsonElement body,
            IWorkerService workerService,
            CancellationToken ct) =>
        {
            var workerId = body.GetProperty("worker_id").GetString()!;
            var ok = await workerService.HeartbeatAsync(workerId, ct);
            return Results.Ok(new { status = ok ? "ok" : "unknown_worker" });
        }).WithOpenApi(op => { op.Summary = "Worker keepalive"; op.Description = "Workers call this every ~30s. Stale workers marked offline."; return op; });
    }
}

internal sealed class OrchestratorApi { }
