using Domain.Shared.Exceptions;

namespace Domain.UnitTests.Shared.Exceptions;

public class EntityNotFoundExceptionTests
{
    [Fact]
    public void Constructor_WithMessage_SetsMessage()
    {
        var message = "Entity not found";

        var exception = new EntityNotFoundException(message);

        Assert.Equal(message, exception.Message);
        Assert.Equal(string.Empty, exception.EntityType);
        Assert.Equal(string.Empty, exception.EntityIdentifier);
    }

    [Fact]
    public void Constructor_WithTypeAndIdentifier_FormatsMessage()
    {
        var entityType = "User";
        var entityId = "12345";

        var exception = new EntityNotFoundException(entityType, entityId);

        Assert.Equal("User with identifier '12345' was not found.", exception.Message);
        Assert.Equal(entityType, exception.EntityType);
        Assert.Equal(entityId, exception.EntityIdentifier);
    }

    [Fact]
    public void Constructor_WithMessageAndInnerException_SetsBoth()
    {
        var message = "Entity not found";
        var innerException = new InvalidOperationException("Database error");

        var exception = new EntityNotFoundException(message, innerException);

        Assert.Equal(message, exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }
}
