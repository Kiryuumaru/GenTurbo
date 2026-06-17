using Domain.Shared.Interfaces;

namespace Application.Shared.Interfaces;

/// <summary>
/// Dispatches domain events to registered handlers.
/// </summary>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Dispatches a single domain event to all matching handlers.
    /// </summary>
    ValueTask DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches multiple domain events to all matching handlers.
    /// </summary>
    ValueTask DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
