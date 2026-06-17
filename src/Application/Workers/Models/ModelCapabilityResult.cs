using Domain.Jobs.Enums;

namespace Application.Workers.Models;

public sealed record ModelCapabilityResult(
    string ModelId,
    MediaType Type,
    int VramRequiredGb,
    IReadOnlyDictionary<string, object?> ParamSchema);
