namespace Domain.Shared.Interfaces;

public interface IEntity
{
    Guid Id { get; }

    Guid RevId { get; }
}
