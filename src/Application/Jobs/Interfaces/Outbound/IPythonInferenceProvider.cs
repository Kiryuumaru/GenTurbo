namespace Application.Jobs.Interfaces.Outbound;

public interface IPythonInferenceProvider
{
    Task<PythonInferenceResult> RunInferenceAsync(
        string model,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);
}

public record PythonInferenceResult(
    string FilePath,
    IReadOnlyDictionary<string, object?> Metadata,
    bool Success,
    string? Error = null,
    string? ErrorType = null);
