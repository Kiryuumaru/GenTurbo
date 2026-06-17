namespace Application.Jobs.Interfaces.Outbound;

/// <summary>
/// Technology-agnostic inference provider. Implemented by infrastructure
/// adapters (CSnakes, REST, gRPC, etc.) to run model inference.
/// Application knows nothing about the underlying runtime.
/// </summary>
public interface IInferenceProvider
{
    Task<InferenceResult> RunInferenceAsync(
        string model,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a model inference call.
/// </summary>
public sealed record InferenceResult(
    string OutputFilePath,
    IReadOnlyDictionary<string, object?> Metadata,
    bool Success,
    string? Error = null,
    string? ErrorType = null);
