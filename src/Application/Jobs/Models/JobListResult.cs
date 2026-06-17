namespace Application.Jobs.Models;

public sealed record JobListResult(IReadOnlyList<JobResult> Jobs, int TotalCount);
