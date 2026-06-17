using Domain.Jobs.Enums;
using Domain.Jobs.Events;
using Domain.Jobs.ValueObjects;
using Domain.Shared.Models;

namespace Domain.Jobs.Entities;

public class JobEntity : AggregateRoot
{
    public string Model { get; private set; }

    public MediaType Type { get; private set; }

    public JobStatus Status { get; private set; }

    public GenerationParams Params { get; private set; }

    public string? OutputUrl { get; private set; }

    public string? Error { get; private set; }

    public string? ErrorType { get; private set; }

    public string? WorkerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public double? InferenceTimeSeconds { get; private set; }

    public int Priority { get; private set; }

    protected JobEntity(
        Guid id,
        string model,
        MediaType type,
        JobStatus status,
        GenerationParams params_,
        string? outputUrl,
        string? error,
        string? errorType,
        string? workerId,
        DateTimeOffset createdAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        DateTimeOffset? cancelledAt,
        double? inferenceTimeSeconds,
        int priority) : base(id)
    {
        Model = model;
        Type = type;
        Status = status;
        Params = params_;
        OutputUrl = outputUrl;
        Error = error;
        ErrorType = errorType;
        WorkerId = workerId;
        CreatedAt = createdAt;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        CancelledAt = cancelledAt;
        InferenceTimeSeconds = inferenceTimeSeconds;
        Priority = priority;
    }

    internal static JobEntity Rehydrate(
        Guid id,
        string model,
        MediaType type,
        JobStatus status,
        GenerationParams params_,
        string? outputUrl,
        string? error,
        string? errorType,
        string? workerId,
        DateTimeOffset createdAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        DateTimeOffset? cancelledAt,
        double? inferenceTimeSeconds,
        int priority)
    {
        return new JobEntity(
            id, model, type, status, params_, outputUrl, error, errorType,
            workerId, createdAt, startedAt, completedAt, cancelledAt,
            inferenceTimeSeconds, priority);
    }

    public static JobEntity Create(string model, MediaType type, GenerationParams params_, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(params_);

        var entity = new JobEntity(
            Guid.NewGuid(),
            model,
            type,
            JobStatus.InQueue,
            params_,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            null,
            null,
            null,
            priority);

        entity.AddDomainEvent(new JobCreatedEvent(entity.Id, entity.Model, entity.Type));
        return entity;
    }

    public void Assign(string workerId)
    {
        WorkerId = workerId;
        Status = JobStatus.Assigned;
        StartedAt = DateTimeOffset.UtcNow;
    }

    public void StartProgress()
    {
        Status = JobStatus.InProgress;
    }

    public void Complete(string outputUrl, double inferenceTimeSeconds)
    {
        OutputUrl = outputUrl;
        InferenceTimeSeconds = inferenceTimeSeconds;
        Status = JobStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new JobCompletedEvent(Id, Model));
    }

    public void Fail(string error, string errorType)
    {
        Error = error;
        ErrorType = errorType;
        Status = JobStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel()
    {
        if (Status == JobStatus.Completed || Status == JobStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel job with status '{Status}'.");

        Status = JobStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        AddDomainEvent(new JobCancelledEvent(Id, Model));
    }
}
