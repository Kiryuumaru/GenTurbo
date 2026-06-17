using Application.Shared.Interfaces;
using Domain.Shared.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.Shared.Services;

internal sealed class DomainEventDispatcher(IEnumerable<IDomainEventHandlerMarker> handlers, ILogger<DomainEventDispatcher> logger) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        var events = domainEvents.ToList();

        foreach (var domainEvent in events)
        {
            foreach (var handler in handlers)
            {
                try
                {
                    await handler.HandleAsync(domainEvent, cancellationToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error handling domain event {EventType}", domainEvent.GetType().Name);
                }
            }
        }
    }
}
