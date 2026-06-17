using System.Text.Json;

namespace Application.Shared.Interfaces.Inbound;

public interface IEmbeddedConfigService
{
    Task<EmbeddedConfig> GetConfig(CancellationToken cancellationToken = default);
}

public sealed record EmbeddedConfig(
    JsonElement SharedConfig,
    JsonElement EnvironmentConfig);
