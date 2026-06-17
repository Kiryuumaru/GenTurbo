namespace Domain.Shared.Exceptions;

public class EntityNotFoundException : DomainException
{
    public string EntityType { get; }

    public string EntityIdentifier { get; }

    public EntityNotFoundException(string entityType, string entityIdentifier)
        : base($"{entityType} with identifier '{entityIdentifier}' was not found.")
    {
        EntityType = entityType;
        EntityIdentifier = entityIdentifier;
    }
}
