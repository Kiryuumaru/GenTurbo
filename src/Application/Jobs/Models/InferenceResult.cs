namespace Application.Jobs.Models;

/// <summary>Result of a model inference call.</summary>
public sealed record InferenceResult(
    string OutputFilePath,
    IReadOnlyDictionary<string, object?> Metadata,
    bool Success,
    string? Error = null,
    string? ErrorType = null);
