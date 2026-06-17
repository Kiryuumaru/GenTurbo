using Domain.Workers.Entities;
using Domain.Workers.Enums;
using Domain.Workers.ValueObjects;

namespace Domain.Workers.Interfaces;

public interface IWorkerRepository
{
    void Add(WorkerEntity entity);

    Task<WorkerEntity?> GetByIdAsync(string workerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkerEntity>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkerEntity>> GetOnlineWorkersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkerEntity>> GetStaleWorkersAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    void Update(WorkerEntity entity);

    void Delete(WorkerEntity entity);

    Task<IReadOnlyList<ModelCapability>> GetAllModelsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetWorkerModelIdsAsync(string workerId, CancellationToken cancellationToken = default);

    Task MarkStaleWorkersOfflineAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    Task PurgeOfflineWorkersAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
