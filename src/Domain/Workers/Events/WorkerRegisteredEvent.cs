using Domain.Shared.Models;

namespace Domain.Workers.Events;

public record WorkerRegisteredEvent(Guid WorkerId, string Name) : DomainEvent;
