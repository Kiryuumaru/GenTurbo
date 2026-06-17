using Application.Jobs.Models;

namespace Application.Jobs.Interfaces.Outbound;

public interface IInferenceProvider
{
    Task<InferenceResult> RunInferenceAsync(string model, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken = default);
}
