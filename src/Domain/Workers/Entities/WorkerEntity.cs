using Domain.Shared.Models;
using Domain.Workers.Enums;
using Domain.Workers.Events;
using Domain.Workers.ValueObjects;

namespace Domain.Workers.Entities;

public class WorkerEntity : AggregateRoot
{
    public string WorkerId { get; private set; }

    public string Name { get; private set; }

    public string Host { get; private set; }

    public WorkerStatus Status { get; private set; }

    public DateTimeOffset LastHeartbeat { get; private set; }

    public DateTimeOffset RegisteredAt { get; private set; }

    public IReadOnlyList<ModelCapability> Models { get; private set; }

    protected WorkerEntity(
        Guid id,
        string workerId,
        string name,
        string host,
        WorkerStatus status,
        DateTimeOffset lastHeartbeat,
        DateTimeOffset registeredAt,
        IReadOnlyList<ModelCapability> models) : base(id)
    {
        WorkerId = workerId;
        Name = name;
        Host = host;
        Status = status;
        LastHeartbeat = lastHeartbeat;
        RegisteredAt = registeredAt;
        Models = models;
    }

    public static WorkerEntity Create(string workerId, string name, string host, IReadOnlyList<ModelCapability> models)
    {
        ArgumentNullException.ThrowIfNull(workerId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(models);

        var now = DateTimeOffset.UtcNow;

        var entity = new WorkerEntity(
            Guid.NewGuid(),
            workerId,
            name,
            host,
            WorkerStatus.Online,
            now,
            now,
            models.ToList());

        entity.AddDomainEvent(new WorkerRegisteredEvent(entity.Id, name));
        return entity;
    }

    public void Heartbeat()
    {
        LastHeartbeat = DateTimeOffset.UtcNow;
        Status = WorkerStatus.Online;
    }

    public void MarkOffline()
    {
        Status = WorkerStatus.Offline;
        AddDomainEvent(new WorkerOfflineEvent(Id, Name));
    }

    public void UpdateModels(IReadOnlyList<ModelCapability> models)
    {
        Models = models.ToList();
    }

    internal static WorkerEntity Rehydrate(
        string workerId,
        string name,
        string host,
        WorkerStatus status,
        DateTimeOffset lastHeartbeat,
        DateTimeOffset registeredAt,
        IReadOnlyList<ModelCapability> models)
    {
        return new WorkerEntity(
            Guid.NewGuid(),
            workerId,
            name,
            host,
            status,
            lastHeartbeat,
            registeredAt,
            models);
    }
}
