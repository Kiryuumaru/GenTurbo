namespace Application.Shared.Interfaces.Outbound;

public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}
