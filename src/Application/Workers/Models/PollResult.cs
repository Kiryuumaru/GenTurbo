namespace Application.Workers.Models;

public sealed record PollResult(
    Guid JobId,
    string Model,
    IReadOnlyDictionary<string, object?> Params);
