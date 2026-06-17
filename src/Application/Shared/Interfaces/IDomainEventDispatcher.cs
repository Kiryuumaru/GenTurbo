namespace Application.Shared.Interfaces;

public interface IDomainEventDispatcher
{
    Task DispatchAsync(IEnumerable<Domain.Shared.Interfaces.IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
