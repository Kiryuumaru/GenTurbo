using Application.Jobs.Interfaces;
using Application.Jobs.Interfaces.Inbound;
using Application.Jobs.Models;
using Application.Shared.Interfaces;
using Application.Workers.Interfaces.Outbound;
using Domain.Jobs.Entities;
using Domain.Jobs.Enums;
using Domain.Jobs.Exceptions;
using Domain.Jobs.Interfaces;
using Domain.Jobs.ValueObjects;
using Domain.Workers.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.Jobs.Services;

internal sealed class JobService : IJobService, IJobOrchestrator
{
    private readonly IJobRepository _jobRepository;
    private readonly IJobUnitOfWork _jobUnitOfWork;
    private readonly IWorkerRepository _workerRepository;
    private readonly IDomainEventDispatcher _domainEventDispatcher;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<JobService> _logger;

    public JobService(IJobRepository jobRepository, IJobUnitOfWork jobUnitOfWork, IWorkerRepository workerRepository, IDomainEventDispatcher domainEventDispatcher, IFileStorage fileStorage, ILogger<JobService> logger)
    {
        _jobRepository = jobRepository;
        _jobUnitOfWork = jobUnitOfWork;
        _workerRepository = workerRepository;
        _domainEventDispatcher = domainEventDispatcher;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<GenerateResponse> SubmitJobAsync(GenerateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var models = await _workerRepository.GetAllModelsAsync(cancellationToken);
        _ = models.FirstOrDefault(m => m.ModelId == request.Model) ?? throw new InvalidOperationException($"Unknown model: {request.Model}");
        var entity = JobEntity.Create(request.Model, models.First(m => m.ModelId == request.Model).Type, GenerationParams.Create(request.Params));
        _jobRepository.Add(entity);
        await _jobUnitOfWork.CommitAsync(cancellationToken);
        await _domainEventDispatcher.DispatchAsync(entity.DomainEvents, cancellationToken);
        entity.ClearDomainEvents();
        _logger.LogInformation("Job {JobId} created for model {Model}", entity.Id, entity.Model);
        return new GenerateResponse(entity.Id, entity.Status.ToString(), entity.Model, entity.CreatedAt);
    }

    public async Task<JobResult?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var entity = await _jobRepository.GetByIdAsync(jobId, cancellationToken);
        return entity is null ? null : MapToResult(entity);
    }

    public async Task<JobListResult> ListJobsAsync(JobStatus? status, string? model, MediaType? type, int limit, int offset, CancellationToken cancellationToken = default)
    {
        var (entities, totalCount) = await _jobRepository.ListAsync(status, model, type, limit, offset, cancellationToken);
        return new JobListResult(entities.Select(MapToResult).ToList(), totalCount);
    }

    public async Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var entity = await _jobRepository.GetByIdAsync(jobId, cancellationToken) ?? throw new JobNotFoundException(jobId.ToString());
        if (entity.Status is JobStatus.Completed or JobStatus.Cancelled) return false;
        entity.Cancel();
        _jobRepository.Update(entity);
        await _jobUnitOfWork.CommitAsync(cancellationToken);
        await _domainEventDispatcher.DispatchAsync(entity.DomainEvents, cancellationToken);
        entity.ClearDomainEvents();
        _logger.LogInformation("Job {JobId} cancelled", entity.Id);
        return true;
    }

    public async Task<JobResult?> DeleteJobAsync(Guid jobId, bool force, CancellationToken cancellationToken = default)
    {
        var entity = await _jobRepository.GetByIdAsync(jobId, cancellationToken) ?? throw new JobNotFoundException(jobId.ToString());
        if (!force && entity.Status is not JobStatus.Completed and not JobStatus.Cancelled)
            throw new InvalidOperationException($"Cannot delete job with status '{entity.Status}'. Use force=true.");
        if (force && entity.Status is not JobStatus.Completed and not JobStatus.Cancelled)
            entity.Cancel();
        var result = MapToResult(entity);
        _jobRepository.Delete(entity);
        await _jobUnitOfWork.CommitAsync(cancellationToken);
        if (entity.OutputUrl is not null) _fileStorage.Delete(entity.OutputUrl);
        _logger.LogInformation("Job {JobId} deleted", entity.Id);
        return result;
    }

    public async Task<JobResult?> GetNextPendingJobAsync(IReadOnlyList<string> capableModels, CancellationToken cancellationToken = default)
    {
        var entity = await _jobRepository.GetNextPendingAsync(capableModels, cancellationToken);
        if (entity is null) return null;
        entity.Assign("system");
        _jobRepository.Update(entity);
        await _jobUnitOfWork.CommitAsync(cancellationToken);
        return MapToResult(entity);
    }

    public async Task<bool> CompleteJobAsync(Guid jobId, string workerId, string outputUrl, double inferenceTimeSeconds, CancellationToken cancellationToken = default)
    {
        var entity = await _jobRepository.GetByIdAsync(jobId, cancellationToken);
        if (entity is null || entity.WorkerId != workerId) return false;
        entity.Complete(outputUrl, inferenceTimeSeconds);
        _jobRepository.Update(entity);
        await _jobUnitOfWork.CommitAsync(cancellationToken);
        await _domainEventDispatcher.DispatchAsync(entity.DomainEvents, cancellationToken);
        entity.ClearDomainEvents();
        _logger.LogInformation("Job {JobId} completed by worker {WorkerId} in {Time}s", jobId, workerId, inferenceTimeSeconds);
        return true;
    }

    public async Task<bool> FailJobAsync(Guid jobId, string workerId, string error, string errorType, CancellationToken cancellationToken = default)
    {
        var entity = await _jobRepository.GetByIdAsync(jobId, cancellationToken);
        if (entity is null || entity.WorkerId != workerId) return false;
        entity.Fail(error, errorType);
        _jobRepository.Update(entity);
        await _jobUnitOfWork.CommitAsync(cancellationToken);
        _logger.LogWarning("Job {JobId} failed: {ErrorType} - {Error}", jobId, errorType, error);
        return true;
    }

    public async Task<bool> SetJobInProgressAsync(Guid jobId, string workerId, CancellationToken cancellationToken = default)
    {
        var entity = await _jobRepository.GetByIdAsync(jobId, cancellationToken);
        if (entity is null || entity.WorkerId != workerId) return false;
        entity.StartProgress();
        _jobRepository.Update(entity);
        await _jobUnitOfWork.CommitAsync(cancellationToken);
        return true;
    }

    public async Task RequeueStaleJobsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await _jobRepository.RequeueStaleJobsAsync(cutoff, cancellationToken);
    }

    public async Task<IReadOnlyList<JobResult>> PurgeExpiredJobsAsync(int retentionHours, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-retentionHours);
        var expiredJobs = await _jobRepository.GetExpiredJobsAsync(cutoff, cancellationToken);
        var results = new List<JobResult>();
        foreach (var entity in expiredJobs)
        {
            results.Add(MapToResult(entity));
            _jobRepository.Delete(entity);
            if (entity.OutputUrl is not null) _fileStorage.Delete(entity.OutputUrl);
        }
        await _jobUnitOfWork.CommitAsync(cancellationToken);
        if (results.Count > 0) _logger.LogInformation("Purged {Count} expired jobs", results.Count);
        return results;
    }

    private static JobResult MapToResult(JobEntity entity) => new(entity.Id, entity.Status.ToString(), entity.Model, entity.Type.ToString(), entity.CreatedAt, entity.StartedAt, entity.CompletedAt, entity.CancelledAt, entity.OutputUrl, entity.Error, entity.ErrorType, entity.WorkerId, entity.InferenceTimeSeconds);
}
