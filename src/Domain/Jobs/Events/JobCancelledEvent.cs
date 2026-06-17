using Domain.Shared.Models;

namespace Domain.Jobs.Events;

public record JobCancelledEvent(Guid JobId, string Model) : DomainEvent;
