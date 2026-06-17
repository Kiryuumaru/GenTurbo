using Domain.Shared.Models;

namespace Domain.Jobs.Events;

public record JobDeletedEvent(Guid JobId, string Model) : DomainEvent;
