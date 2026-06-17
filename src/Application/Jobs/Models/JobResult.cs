using Domain.Jobs.Enums;

namespace Application.Jobs.Models;

public sealed record JobResult(
    Guid RequestId,
    string Status,
    string Model,
    string Type,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    DateTimeOffset? CancelledAt = null,
    string? OutputUrl = null,
    string? Error = null,
    string? ErrorType = null,
    string? WorkerId = null,
    double? InferenceTimeSeconds = null);
