using Domain.Shared.Exceptions;

namespace Domain.Jobs.Exceptions;

public class JobNotFoundException : EntityNotFoundException
{
    public JobNotFoundException(string jobId)
        : base("Job", jobId) { }
}
