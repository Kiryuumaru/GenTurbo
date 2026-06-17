using Domain.Workers.Enums;

namespace Application.Workers.Models;

public sealed record WorkerResult(
    string WorkerId,
    string Name,
    WorkerStatus Status,
    IReadOnlyList<ModelCapabilityResult> Models,
    DateTimeOffset? LastHeartbeat);
