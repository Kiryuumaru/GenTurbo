using Domain.Shared.Interfaces;

namespace Application.Shared.Interfaces;

/// <summary>
/// Defines a handler for domain events.
/// </summary>
public interface IDomainEventHandler
{
    /// <summary>
    /// Determines whether this handler can handle the specified domain event.
    /// </summary>
    bool CanHandle(IDomainEvent domainEvent);

    /// <summary>
    /// Handles the specified domain event.
    /// </summary>
    ValueTask HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines a strongly-typed handler for domain events of type <typeparamref name="TEvent"/>.
/// </summary>
public interface IDomainEventHandler<in TEvent> : IDomainEventHandler where TEvent : IDomainEvent
{
    /// <summary>
    /// Handles the specified domain event.
    /// </summary>
    ValueTask HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}
