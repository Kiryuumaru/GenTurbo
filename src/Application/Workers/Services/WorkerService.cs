using Application.Workers.Interfaces.Inbound;
using Application.Workers.Models;
using Application.Jobs.Interfaces;
using Domain.Workers.Entities;
using Domain.Workers.Interfaces;
using Domain.Workers.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Application.Workers.Services;

internal sealed class WorkerService(
    IWorkerRepository workerRepository,
    IWorkerUnitOfWork workerUnitOfWork,
    IJobOrchestrator jobOrchestrator,
    ILogger<WorkerService> logger) : IWorkerService
{
    public async Task RegisterWorkerAsync(
        string workerId,
        string name,
        string host,
        IReadOnlyList<ModelCapability> models,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workerId);
        ArgumentNullException.ThrowIfNull(name);

        var existing = await workerRepository.GetByIdAsync(workerId, cancellationToken);
        if (existing is not null)
        {
            existing.UpdateModels(models);
            existing.Heartbeat();
            workerRepository.Update(existing);
        }
        else
        {
            var entity = WorkerEntity.Create(workerId, name, host, models);
            workerRepository.Add(entity);
        }

        await workerUnitOfWork.CommitAsync(cancellationToken);
        logger.LogInformation("Worker {WorkerId} ({Name}) registered with {Count} model(s)", workerId, name, models.Count);
    }

    public async Task<bool> HeartbeatAsync(string workerId, CancellationToken cancellationToken = default)
    {
        var entity = await workerRepository.GetByIdAsync(workerId, cancellationToken);
        if (entity is null)
            return false;

        entity.Heartbeat();
        workerRepository.Update(entity);
        await workerUnitOfWork.CommitAsync(cancellationToken);

        return true;
    }

    public async Task<PollResult?> PollForJobAsync(string workerId, CancellationToken cancellationToken = default)
    {
        var modelIds = await workerRepository.GetWorkerModelIdsAsync(workerId, cancellationToken);
        if (modelIds.Count == 0)
            return null;

        var job = await jobOrchestrator.GetNextPendingJobAsync(modelIds, cancellationToken);
        if (job is null)
            return null;

        await jobOrchestrator.SetJobInProgressAsync(job.RequestId, workerId, cancellationToken);

        logger.LogInformation("Worker {WorkerId} claimed job {JobId}", workerId, job.RequestId);

        return new PollResult(job.RequestId, job.Model, new Dictionary<string, object?>());
    }

    public async Task<IReadOnlyList<WorkerResult>> GetAllWorkersAsync(CancellationToken cancellationToken = default)
    {
        var entities = await workerRepository.GetAllAsync(cancellationToken);
        return entities.Select(MapWorkerToResult).ToList();
    }

    public async Task<IReadOnlyList<ModelCapabilityResult>> GetAllModelsAsync(CancellationToken cancellationToken = default)
    {
        var models = await workerRepository.GetAllModelsAsync(cancellationToken);
        return models.Select(m => new ModelCapabilityResult(m.ModelId, m.Type, m.VramRequiredGb, m.ParamSchema)).ToList();
    }

    public async Task<ModelCapabilityResult?> GetModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var models = await workerRepository.GetAllModelsAsync(cancellationToken);
        var model = models.FirstOrDefault(m => m.ModelId == modelId);
        if (model is null)
            return null;

        return new ModelCapabilityResult(model.ModelId, model.Type, model.VramRequiredGb, model.ParamSchema);
    }

    public async Task MarkStaleWorkersAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-10);
        await workerRepository.MarkStaleWorkersOfflineAsync(cutoff, cancellationToken);

        var purgeCutoff = DateTimeOffset.UtcNow.AddDays(-7);
        await workerRepository.PurgeOfflineWorkersAsync(purgeCutoff, cancellationToken);
    }

    private static WorkerResult MapWorkerToResult(WorkerEntity entity)
    {
        return new WorkerResult(
            entity.WorkerId,
            entity.Name,
            entity.Status,
            entity.Models.Select(m => new ModelCapabilityResult(m.ModelId, m.Type, m.VramRequiredGb, m.ParamSchema)).ToList(),
            entity.LastHeartbeat);
    }
}
