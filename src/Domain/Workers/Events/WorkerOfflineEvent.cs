using Domain.Shared.Models;

namespace Domain.Workers.Events;

public record WorkerOfflineEvent(Guid WorkerId, string Name) : DomainEvent;
