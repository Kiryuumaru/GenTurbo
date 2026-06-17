using Application.Shared.Interfaces;
using Domain.Shared.Interfaces;

namespace Application.Shared.Models;

public abstract class DomainEventHandler<TEvent> : IDomainEventHandler<TEvent>, IDomainEventHandlerMarker
    where TEvent : IDomainEvent
{
    public Task HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (domainEvent is TEvent typedEvent)
            return HandleAsync(typedEvent, cancellationToken);

        return Task.CompletedTask;
    }

    public abstract Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
