namespace Domain.Shared.Exceptions;

public class EntityNotFoundException : DomainException
{
    public string EntityType { get; }

    public string EntityIdentifier { get; }

    public EntityNotFoundException(string message)
        : base(message)
    {
        EntityType = string.Empty;
        EntityIdentifier = string.Empty;
    }

    public EntityNotFoundException(string entityType, string entityIdentifier)
        : base($"{entityType} with identifier '{entityIdentifier}' was not found.")
    {
        EntityType = entityType;
        EntityIdentifier = entityIdentifier;
    }

    public EntityNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
        EntityType = string.Empty;
        EntityIdentifier = string.Empty;
    }
}
