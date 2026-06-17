using Application.Jobs.Models;

namespace Application.Workers.Interfaces.Inbound;

public interface IWorkerInferenceService
{
    Task<InferenceResult> RunInferenceAsync(string model, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default);
}
