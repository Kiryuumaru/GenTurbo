using Application.Shared.Models;
using Domain.Jobs.Events;
using Microsoft.Extensions.Logging;

namespace Application.Jobs.EventHandlers;

internal sealed class LogJobCompletedHandler(ILogger<LogJobCompletedHandler> logger) : DomainEventHandler<JobCompletedEvent>
{
    public override ValueTask HandleAsync(JobCompletedEvent domainEvent, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Job {JobId} completed for model {Model}", domainEvent.JobId, domainEvent.Model);
        return ValueTask.CompletedTask;
    }
}
