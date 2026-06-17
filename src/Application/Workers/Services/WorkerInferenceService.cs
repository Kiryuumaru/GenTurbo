using Application.Jobs.Models;
using Application.Jobs.Interfaces.Outbound;
using Application.Workers.Interfaces.Inbound;

namespace Application.Workers.Services;

internal sealed class WorkerInferenceService(IInferenceProvider inferenceProvider) : IWorkerInferenceService
{
    public Task<InferenceResult> RunInferenceAsync(string model, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        => inferenceProvider.RunInferenceAsync(model, parameters, cancellationToken);
}
