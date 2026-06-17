using Domain.Shared.Models;

namespace Domain.Jobs.Events;

public record JobCompletedEvent(Guid JobId, string Model) : DomainEvent;
