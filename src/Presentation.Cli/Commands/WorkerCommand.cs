using Application.Jobs.Interfaces.Outbound;
using ApplicationBuilderHelpers;
using ApplicationBuilderHelpers.Attributes;
using Domain.Workers.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace Presentation.Cli.Commands;

[Command("Run the GenTurbo worker — polls orchestrator, runs inference, uploads results.")]
internal class WorkerCommand : Build.BaseCommand<HostApplicationBuilder>
{
    [CommandOption('u', "orchestrator-url", EnvironmentVariable = "GEN_TURBO_ORCHESTRATOR_URL",
        Description = "Orchestrator API base URL.", Required = true)]
    public string OrchestratorUrl { get; set; } = "http://localhost:7860";

    [CommandOption('i', "worker-id", EnvironmentVariable = "WORKER_ID",
        Description = "Unique worker identifier.", Required = true)]
    public string WorkerId { get; set; } = "dev-worker";

    [CommandOption('n', "worker-name", EnvironmentVariable = "WORKER_NAME",
        Description = "Human-readable worker name.")]
    public string WorkerName { get; set; } = "dev";

    [CommandOption("model", EnvironmentVariable = "WORKER_MODELS",
        Description = "Comma-separated model IDs this worker can run.")]
    public string Models { get; set; } = "z-image-turbo";

    [CommandOption("vram-per-model", EnvironmentVariable = "WORKER_VRAM_PER_MODEL",
        Description = "VRAM required per model in GB.")]
    public int VramPerModel { get; set; } = 14;

    protected override ValueTask<HostApplicationBuilder> ApplicationBuilder(CancellationToken stoppingToken)
    {
        var builder = Host.CreateApplicationBuilder();
        return new ValueTask<HostApplicationBuilder>(builder);
    }

    protected override async ValueTask Run(ApplicationHost<HostApplicationBuilder> applicationHost, CancellationTokenSource cancellationTokenSource)
    {
        await base.Run(applicationHost, cancellationTokenSource);

        using var scope = applicationHost.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<WorkerCommand>>();
        var inferenceProvider = scope.ServiceProvider.GetRequiredService<IPythonInferenceProvider>();

        logger.LogInformation("Worker {WorkerId} ({Name}) → {Url}", WorkerId, WorkerName, OrchestratorUrl);

        var modelIds = Models.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var capabilities = modelIds.Select(m => ModelCapability.Create(
            m, Domain.Jobs.Enums.MediaType.Image, VramPerModel, new Dictionary<string, object?>())).ToList();

        using var http = new HttpClient { BaseAddress = new Uri(OrchestratorUrl) };
        var ct = cancellationTokenSource.Token;

        await RegisterWorkerAsync(http, capabilities, logger, ct);

        var lastHeartbeat = DateTimeOffset.UtcNow;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if ((DateTimeOffset.UtcNow - lastHeartbeat).TotalSeconds > 30)
                {
                    await SendHeartbeatAsync(http, logger, ct);
                    lastHeartbeat = DateTimeOffset.UtcNow;
                }

                var job = await PollForJobAsync(http, logger, ct);
                if (job is null)
                {
                    await Task.Delay(5000, ct);
                    continue;
                }

                logger.LogInformation("Job {JobId} model {Model}", job.JobId, job.Model);

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = await inferenceProvider.RunInferenceAsync(job.Model, job.Params, ct);
                sw.Stop();

                if (result.Success)
                {
                    await UploadResultAsync(http, job.JobId, result.FilePath, logger, ct);
                    logger.LogInformation("Job {JobId} done in {Time:F1}s", job.JobId, sw.Elapsed.TotalSeconds);
                }
                else
                {
                    await ReportErrorAsync(http, job.JobId, result.Error ?? "unknown", result.ErrorType ?? "UNKNOWN", ct);
                    logger.LogError("Job {JobId} failed: {Error}", job.JobId, result.Error);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker loop error");
                await Task.Delay(5000, ct);
            }
        }

        logger.LogInformation("Worker shutdown complete");
    }

    private async Task RegisterWorkerAsync(HttpClient http, IReadOnlyList<ModelCapability> capabilities, ILogger logger, CancellationToken ct)
    {
        var payload = new
        {
            worker_id = WorkerId,
            name = WorkerName,
            models = capabilities.Select(m => new
            {
                id = m.ModelId,
                type = m.Type.ToString().ToLowerInvariant(),
                vram_required_gb = m.VramRequiredGb,
                param_schema = m.ParamSchema
            })
        };

        var resp = await http.PostAsJsonAsync("/worker/register", payload, ct);
        resp.EnsureSuccessStatusCode();
        logger.LogInformation("Registered with orchestrator — {Count} model(s)", capabilities.Count);
    }

    private async Task<PollJob?> PollForJobAsync(HttpClient http, ILogger logger, CancellationToken ct)
    {
        try
        {
            var resp = await http.PostAsJsonAsync("/worker/poll", new { worker_id = WorkerId }, ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
            var jobEl = json.GetProperty("job");
            if (jobEl.ValueKind == JsonValueKind.Null)
                return null;

            return new PollJob(
                Guid.Parse(jobEl.GetProperty("job_id").GetString()!),
                jobEl.GetProperty("model").GetString()!,
                new Dictionary<string, object?>());
        }
        catch
        {
            return null;
        }
    }

    private async Task SendHeartbeatAsync(HttpClient http, ILogger logger, CancellationToken ct)
    {
        try { await http.PostAsJsonAsync("/worker/heartbeat", new { worker_id = WorkerId }, ct); } catch { }
    }

    private async Task UploadResultAsync(HttpClient http, Guid jobId, string filePath, ILogger logger, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(WorkerId), "worker_id");
        content.Add(new StringContent(jobId.ToString()), "job_id");
        content.Add(new StringContent("{}"), "metadata_json");

        var fileBytes = await File.ReadAllBytesAsync(filePath, ct);
        var fc = new ByteArrayContent(fileBytes);
        fc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(fc, "file", Path.GetFileName(filePath));

        var resp = await http.PostAsync("/worker/complete", content, ct);
        resp.EnsureSuccessStatusCode();
    }

    private async Task ReportErrorAsync(HttpClient http, Guid jobId, string error, string errorType, CancellationToken ct)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(WorkerId), "worker_id");
        content.Add(new StringContent(jobId.ToString()), "job_id");
        content.Add(new StringContent(error), "error");
        content.Add(new StringContent(errorType), "error_type");
        content.Add(new StringContent("{}"), "metadata_json");
        await http.PostAsync("/worker/complete", content, ct);
    }

    private sealed record PollJob(Guid JobId, string Model, IReadOnlyDictionary<string, object?> Params);
}
