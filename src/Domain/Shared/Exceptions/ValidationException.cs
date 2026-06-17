namespace Domain.Shared.Exceptions;

public class ValidationException : DomainException
{
    public string PropertyName { get; }

    public ValidationException(string propertyName, string message)
        : base(message)
    {
        PropertyName = propertyName;
    }
}
