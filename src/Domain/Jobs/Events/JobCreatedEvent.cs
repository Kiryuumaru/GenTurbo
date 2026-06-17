using Domain.Jobs.Enums;
using Domain.Shared.Models;

namespace Domain.Jobs.Events;

public record JobCreatedEvent(Guid JobId, string Model, MediaType Type) : DomainEvent;
