namespace Application.EmbeddedConfig.Interfaces.Inbound;

/// <summary>
/// Application service for retrieving environment-specific and shared embedded configuration
/// that was encrypted at build time and decrypted at runtime.
/// </summary>
public interface IEmbeddedConfigService
{
    Task<Models.EmbeddedConfigResult> GetConfig(CancellationToken cancellationToken);
}
