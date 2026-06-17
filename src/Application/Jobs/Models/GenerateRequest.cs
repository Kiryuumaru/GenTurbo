using Domain.Jobs.Enums;

namespace Application.Jobs.Models;

public sealed record GenerateRequest(string Model, IReadOnlyDictionary<string, object?> Params);
