namespace Domain.AppEnvironment.Models;

/// <summary>
/// Represents a complete application environment configuration.
/// </summary>
public class AppEnvironment
{
    /// <summary>
    /// Gets the full name of the deployment environment.
    /// </summary>
    public required string Full { get; init; }

    /// <summary>
    /// Gets the abbreviated short name for the deployment environment.
    /// </summary>
    public required string Short { get; init; }

    /// <summary>
    /// Gets the environment tag used for deployment identification and version management.
    /// </summary>
    public required string Tag { get; init; }
}
