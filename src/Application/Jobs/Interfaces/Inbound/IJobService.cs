using Application.Jobs.Models;
using Domain.Jobs.Enums;

namespace Application.Jobs.Interfaces.Inbound;

public interface IJobService
{
    Task<GenerateResponse> SubmitJobAsync(GenerateRequest request, CancellationToken cancellationToken = default);

    Task<JobResult?> GetJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<JobListResult> ListJobsAsync(
        JobStatus? status,
        string? model,
        MediaType? type,
        int limit,
        int offset,
        CancellationToken cancellationToken = default);

    Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken = default);

    Task<JobResult?> DeleteJobAsync(Guid jobId, bool force, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobResult>> PurgeExpiredJobsAsync(int retentionHours, CancellationToken cancellationToken = default);
}
