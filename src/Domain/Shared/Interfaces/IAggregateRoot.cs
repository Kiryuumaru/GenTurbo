namespace Domain.Shared.Interfaces;

public interface IAggregateRoot
{
    IReadOnlyList<IDomainEvent> DomainEvents { get; }

    void AddDomainEvent(IDomainEvent domainEvent);

    void RemoveDomainEvent(IDomainEvent domainEvent);

    void ClearDomainEvents();
}
