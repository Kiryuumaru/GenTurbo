using Application.Workers.Models;
using Domain.Workers.ValueObjects;

namespace Application.Workers.Interfaces.Inbound;

public interface IWorkerService
{
    Task RegisterWorkerAsync(string workerId, string name, string host, IReadOnlyList<ModelCapability> models, CancellationToken cancellationToken = default);

    Task<bool> HeartbeatAsync(string workerId, CancellationToken cancellationToken = default);

    Task<PollResult?> PollForJobAsync(string workerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkerResult>> GetAllWorkersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelCapabilityResult>> GetAllModelsAsync(CancellationToken cancellationToken = default);

    Task<ModelCapabilityResult?> GetModelAsync(string modelId, CancellationToken cancellationToken = default);

    Task MarkStaleWorkersAsync(CancellationToken cancellationToken = default);
}
