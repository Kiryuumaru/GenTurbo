using Domain.Shared.Interfaces;

namespace Application.Shared.Interfaces;

public interface IDomainEventHandlerMarker
{
    Task HandleAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default);
}
