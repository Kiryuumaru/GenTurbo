namespace Application.Jobs.Models;

public sealed record GenerateResponse(Guid RequestId, string Status, string Model, DateTimeOffset CreatedAt);
