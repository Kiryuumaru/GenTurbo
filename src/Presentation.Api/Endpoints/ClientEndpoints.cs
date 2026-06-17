using Application.Jobs.Interfaces;
using Application.Jobs.Interfaces.Inbound;
using Application.Jobs.Models;
using Application.Workers.Interfaces.Inbound;
using Application.Workers.Interfaces.Outbound;
using Application.Workers.Models;
using Domain.Jobs.Enums;
using Domain.Workers.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Presentation.Api.Endpoints;

internal static class ClientEndpoints
{
    public static void Map(WebApplication app)
    {
        var client = app.MapGroup("/");

        client.MapGet("/", () => Results.Content(
            """<!DOCTYPE html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>gen-turbo API</title><style>body{margin:0;background:#0d0d0d}</style></head><body><script id="api-reference" data-url="/openapi.json"></script><script src="https://cdn.jsdelivr.net/npm/@scalar/api-reference"></script></body></html>""",
            "text/html"))
        .ExcludeFromDescription()
        .WithOpenApi(op => { op.Summary = "Scalar API Reference"; op.Description = "Interactive API documentation powered by Scalar."; return op; });

        client.MapPost("/generate", async (
            [FromBody] GenerateRequest request,
            IJobService jobService,
            IWorkerService workerService,
            CancellationToken ct) =>
        {
            var models = await workerService.GetAllModelsAsync(ct);
            if (!models.Any(m => m.ModelId == request.Model))
                return Results.Problem($"Unknown model: {request.Model}", statusCode: 400);

            var result = await jobService.SubmitJobAsync(request, ct);
            return Results.Created($"/jobs/{result.RequestId}", result);
        }).WithTags("client")
          .WithOpenApi(op => { op.Summary = "Submit a generation job"; op.Description = "Submit an image, video, audio, or 3D generation job. Returns immediately with a request_id."; return op; });

        client.MapGet("/jobs", async (
            [FromQuery] string? status,
            [FromQuery] string? model,
            [FromQuery] string? type,
            [FromQuery] int? limit,
            [FromQuery] int? offset,
            IJobService jobService,
            CancellationToken ct) =>
        {
            var jobStatus = status is not null ? Enum.Parse<JobStatus>(status, ignoreCase: true) : (JobStatus?)null;
            var mediaType = type is not null ? Enum.Parse<MediaType>(type, ignoreCase: true) : (MediaType?)null;

            var result = await jobService.ListJobsAsync(jobStatus, model, mediaType, limit ?? 20, offset ?? 0, ct);
            return Results.Ok(result);
        }).WithTags("client")
          .WithOpenApi(op => { op.Summary = "List jobs"; op.Description = "Returns jobs ordered by creation time (newest first). Filterable by status, model, and media type."; return op; });

        client.MapGet("/jobs/{jobId:guid}", async (
            Guid jobId,
            IJobService jobService,
            CancellationToken ct) =>
        {
            var job = await jobService.GetJobAsync(jobId, ct);
            if (job is null)
                return Results.Problem("Job not found", statusCode: 404);
            return Results.Ok(job);
        }).WithTags("client")
          .WithOpenApi(op => { op.Summary = "Get job status"; op.Description = "Returns full job details: status, timestamps, worker assignment, and output (if completed)."; return op; });

        client.MapPut("/jobs/{jobId:guid}/cancel", async (
            Guid jobId,
            IJobService jobService,
            CancellationToken ct) =>
        {
            var job = await jobService.GetJobAsync(jobId, ct);
            if (job is null)
                return Results.Problem("Job not found", statusCode: 404);

            var ok = await jobService.CancelJobAsync(jobId, ct);
            if (!ok)
                return Results.Problem($"Cannot cancel job with status {job.Status}", statusCode: 400);

            return Results.Ok(new { request_id = jobId, status = "CANCELLED" });
        }).WithTags("client")
          .WithOpenApi(op => { op.Summary = "Cancel a job"; op.Description = "Cancel a queued or in-progress job. The worker will stop processing."; return op; });

        client.MapDelete("/jobs/{jobId:guid}", async (
            Guid jobId,
            [FromQuery] bool? force,
            IJobService jobService,
            IFileStorage fileStorage,
            CancellationToken ct) =>
        {
            try
            {
                var result = await jobService.DeleteJobAsync(jobId, force == true, ct);
                if (result is null)
                    return Results.Problem("Job not found", statusCode: 404);

                if (result.OutputUrl is not null)
                    fileStorage.Delete(result.OutputUrl);

                return Results.Ok(new { request_id = jobId, status = "DELETED" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: 400);
            }
        }).WithTags("client")
          .WithOpenApi(op => { op.Summary = "Permanently delete a job"; op.Description = "Deletes the job record and generated file. Use ?force=true to cancel+delete in one call."; return op; });

        client.MapGet("/models", async (IWorkerService workerService, CancellationToken ct) =>
        {
            var models = await workerService.GetAllModelsAsync(ct);
            return Results.Ok(new { models, count = models.Count });
        }).WithTags("client")
          .WithOpenApi(op => { op.Summary = "List available models"; op.Description = "Returns all models currently available across connected workers with param schemas."; return op; });

        client.MapGet("/models/{modelId}", async (
            string modelId,
            IWorkerService workerService,
            CancellationToken ct) =>
        {
            var model = await workerService.GetModelAsync(modelId, ct);
            if (model is null)
                return Results.Problem("Model not found", statusCode: 404);
            return Results.Ok(model);
        }).WithTags("client")
          .WithOpenApi(op => { op.Summary = "Get model details"; op.Description = "Returns single model's metadata including full param_schema."; return op; });

        client.MapGet("/files/{filename}", (string filename, IFileStorage fileStorage) =>
        {
            var url = $"/files/{filename}";
            if (!fileStorage.Exists(url))
                return Results.Problem("File not found", statusCode: 404);

            var path = fileStorage.ResolvePath(url);
            return Results.File(path);
        }).WithTags("client")
          .WithOpenApi(op => { op.Summary = "Download a generated file"; op.Description = "Serve a generated output file (image PNG, video, audio, 3D model)."; return op; });

        client.MapGet("/health", async (IWorkerService workerService, CancellationToken ct) =>
        {
            var workers = await workerService.GetAllWorkersAsync(ct);
            return Results.Ok(new
            {
                status = "ok",
                workers = workers.ToDictionary(w => w.WorkerId, w => new { w.Name, w.Status, w.Models })
            });
        }).WithTags("client")
          .WithOpenApi(op => { op.Summary = "Health check"; op.Description = "Returns orchestrator health and connected worker information."; return op; });
    }
}
