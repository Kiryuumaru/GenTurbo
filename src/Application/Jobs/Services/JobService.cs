using Application.Jobs.Interfaces;
using Application.Jobs.Interfaces.Inbound;
using Application.Jobs.Interfaces.Outbound;
using Application.Jobs.Models;
using Application.Shared.Interfaces;
using Domain.Jobs.Entities;
using Domain.Jobs.Enums;
using Domain.Jobs.Exceptions;
using Domain.Jobs.Interfaces;
using Domain.Jobs.ValueObjects;
using Domain.Workers.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.Jobs.Services;

internal sealed class JobService(
    IJobRepository jobRepository,
    IJobUnitOfWork jobUnitOfWork,
    IWorkerRepository workerRepository,
    IDomainEventDispatcher domainEventDispatcher,
    ILogger<JobService> logger) : IJobService, IJobOrchestrator
{
    public async Task<GenerateResponse> SubmitJobAsync(GenerateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var models = await workerRepository.GetAllModelsAsync(cancellationToken);
        var modelInfo = models.FirstOrDefault(m => m.ModelId == request.Model)
            ?? throw new InvalidOperationException($"Unknown model: {request.Model}");

        var generationParams = GenerationParams.Create(request.Params);
        var entity = JobEntity.Create(request.Model, modelInfo.Type, generationParams);

        jobRepository.Add(entity);
        await jobUnitOfWork.CommitAsync(cancellationToken);

        await domainEventDispatcher.DispatchAsync(entity.DomainEvents, cancellationToken);
        entity.ClearDomainEvents();

        logger.LogInformation("Job {JobId} created for model {Model}", entity.Id, entity.Model);

        return new GenerateResponse(entity.Id, entity.Status.ToString(), entity.Model, entity.CreatedAt);
    }

    public async Task<JobResult?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var entity = await jobRepository.GetByIdAsync(jobId, cancellationToken);
        if (entity is null)
            return null;

        return MapToResult(entity);
    }

    public async Task<JobListResult> ListJobsAsync(
        JobStatus? status,
        string? model,
        MediaType? type,
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        var (entities, totalCount) = await jobRepository.ListAsync(status, model, type, limit, offset, cancellationToken);
        return new JobListResult(entities.Select(MapToResult).ToList(), totalCount);
    }

    public async Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var entity = await jobRepository.GetByIdAsync(jobId, cancellationToken)
            ?? throw new JobNotFoundException(jobId.ToString());

        if (entity.Status == JobStatus.Completed || entity.Status == JobStatus.Cancelled)
            return false;

        entity.Cancel();
        jobRepository.Update(entity);
        await jobUnitOfWork.CommitAsync(cancellationToken);

        await domainEventDispatcher.DispatchAsync(entity.DomainEvents, cancellationToken);
        entity.ClearDomainEvents();

        logger.LogInformation("Job {JobId} cancelled", entity.Id);
        return true;
    }

    public async Task<JobResult?> DeleteJobAsync(Guid jobId, bool force, CancellationToken cancellationToken = default)
    {
        var entity = await jobRepository.GetByIdAsync(jobId, cancellationToken)
            ?? throw new JobNotFoundException(jobId.ToString());

        if (!force && entity.Status != JobStatus.Completed && entity.Status != JobStatus.Cancelled)
            throw new InvalidOperationException($"Cannot delete job with status '{entity.Status}'. Use force=true to cancel and delete.");

        if (force && entity.Status != JobStatus.Completed && entity.Status != JobStatus.Cancelled)
        {
            entity.Cancel();
        }

        var result = MapToResult(entity);
        jobRepository.Delete(entity);
        await jobUnitOfWork.CommitAsync(cancellationToken);

        logger.LogInformation("Job {JobId} deleted", entity.Id);
        return result;
    }

    public async Task<JobResult?> GetNextPendingJobAsync(IReadOnlyList<string> capableModels, CancellationToken cancellationToken = default)
    {
        var entity = await jobRepository.GetNextPendingAsync(capableModels, cancellationToken);
        if (entity is null)
            return null;

        entity.Assign("pending");
        jobRepository.Update(entity);
        await jobUnitOfWork.CommitAsync(cancellationToken);

        return MapToResult(entity);
    }

    public async Task<bool> CompleteJobAsync(
        Guid jobId,
        string workerId,
        string outputUrl,
        double inferenceTimeSeconds,
        CancellationToken cancellationToken = default)
    {
        var entity = await jobRepository.GetByIdAsync(jobId, cancellationToken);
        if (entity is null || entity.WorkerId != workerId)
            return false;

        entity.Complete(outputUrl, inferenceTimeSeconds);
        jobRepository.Update(entity);
        await jobUnitOfWork.CommitAsync(cancellationToken);

        await domainEventDispatcher.DispatchAsync(entity.DomainEvents, cancellationToken);
        entity.ClearDomainEvents();

        logger.LogInformation("Job {JobId} completed by worker {WorkerId} in {Time}s", jobId, workerId, inferenceTimeSeconds);
        return true;
    }

    public async Task<bool> FailJobAsync(
        Guid jobId,
        string workerId,
        string error,
        string errorType,
        CancellationToken cancellationToken = default)
    {
        var entity = await jobRepository.GetByIdAsync(jobId, cancellationToken);
        if (entity is null || entity.WorkerId != workerId)
            return false;

        entity.Fail(error, errorType);
        jobRepository.Update(entity);
        await jobUnitOfWork.CommitAsync(cancellationToken);

        logger.LogWarning("Job {JobId} failed: {ErrorType} - {Error}", jobId, errorType, error);
        return true;
    }

    public async Task<bool> SetJobInProgressAsync(Guid jobId, string workerId, CancellationToken cancellationToken = default)
    {
        var entity = await jobRepository.GetByIdAsync(jobId, cancellationToken);
        if (entity is null || entity.WorkerId != workerId)
            return false;

        entity.StartProgress();
        jobRepository.Update(entity);
        await jobUnitOfWork.CommitAsync(cancellationToken);

        return true;
    }

    public async Task RequeueStaleJobsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await jobRepository.RequeueStaleJobsAsync(cutoff, cancellationToken);
    }

    public async Task<IReadOnlyList<JobResult>> PurgeExpiredJobsAsync(int retentionHours, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-retentionHours);
        var expiredJobs = await jobRepository.GetExpiredJobsAsync(cutoff, cancellationToken);

        var results = new List<JobResult>();
        foreach (var entity in expiredJobs)
        {
            results.Add(MapToResult(entity));
            jobRepository.Delete(entity);
        }

        await jobUnitOfWork.CommitAsync(cancellationToken);

        if (results.Count > 0)
            logger.LogInformation("Purged {Count} expired jobs", results.Count);

        return results;
    }

    private static JobResult MapToResult(JobEntity entity)
    {
        return new JobResult(
            entity.Id,
            entity.Status.ToString(),
            entity.Model,
            entity.Type.ToString(),
            entity.CreatedAt,
            entity.StartedAt,
            entity.CompletedAt,
            entity.CancelledAt,
            entity.OutputUrl,
            entity.Error,
            entity.ErrorType,
            entity.WorkerId,
            entity.InferenceTimeSeconds);
    }
}
