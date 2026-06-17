using Domain.Shared.Interfaces;

namespace Domain.Shared.Models;

public abstract class Entity : IEntity
{
    public Guid Id { get; protected set; }

    public Guid RevId { get; protected set; }

    protected Entity(Guid id)
    {
        Id = id;
        RevId = Guid.NewGuid();
    }
}
