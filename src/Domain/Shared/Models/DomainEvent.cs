using Domain.Shared.Interfaces;

namespace Domain.Shared.Models;

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();

    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
