using Application.Jobs.Models;

namespace Application.Jobs.Interfaces;

internal interface IJobOrchestrator
{
    Task<JobResult?> GetNextPendingJobAsync(IReadOnlyList<string> capableModels, CancellationToken cancellationToken = default);

    Task<bool> SetJobInProgressAsync(Guid jobId, string workerId, CancellationToken cancellationToken = default);

    Task<bool> CompleteJobAsync(Guid jobId, string workerId, string outputUrl, double inferenceTimeSeconds, CancellationToken cancellationToken = default);

    Task<bool> FailJobAsync(Guid jobId, string workerId, string error, string errorType, CancellationToken cancellationToken = default);

    Task RequeueStaleJobsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
