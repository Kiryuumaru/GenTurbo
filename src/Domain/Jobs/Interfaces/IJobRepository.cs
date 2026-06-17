using Domain.Jobs.Entities;
using Domain.Jobs.Enums;

namespace Domain.Jobs.Interfaces;

public interface IJobRepository
{
    void Add(JobEntity entity);

    Task<JobEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<JobEntity?> GetNextPendingAsync(IReadOnlyList<string> capableModels, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobEntity>> GetByStatusAsync(JobStatus status, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobEntity>> GetExpiredJobsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobEntity>> GetStaleJobsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<JobEntity> Jobs, int TotalCount)> ListAsync(
        JobStatus? status,
        string? model,
        MediaType? type,
        int limit,
        int offset,
        CancellationToken cancellationToken = default);

    void Update(JobEntity entity);

    void Delete(JobEntity entity);

    Task RequeueStaleJobsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
