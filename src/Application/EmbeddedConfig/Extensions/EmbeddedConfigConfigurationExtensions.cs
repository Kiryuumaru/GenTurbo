using Application.EmbeddedConfig.Utilities;
using Application.Shared.Interfaces.Inbound;
using ApplicationBuilderHelpers.Extensions;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Nodes;

namespace Application.EmbeddedConfig.Extensions;

/// <summary>
/// Configuration extensions for embedded config. Supports loading encrypted build paylods
/// and navigating nested config structures.
/// </summary>
public static class EmbeddedConfigConfigurationExtensions
{
    private const string EmbeddedConfigKey = "RUNTIME_EMBEDDED_CONFIG";

    extension(IConfiguration configuration)
    {
        public JsonObject EmbeddedConfig
        {
            get
            {
                var jsonString = configuration.GetRefValue(EmbeddedConfigKey);
                return JsonNode.Parse(jsonString)?.AsObject()
                    ?? new JsonObject();
            }
            set => configuration[EmbeddedConfigKey] = value.ToJsonString();
        }
    }

    /// <summary>
    /// Decrypts the encrypted build payload from application constants and loads it into configuration.
    /// Called from BaseCommand.AddConfigurations to make encrypted config available to all layers.
    /// </summary>
    public static void LoadEncryptedEmbeddedConfig(this IConfiguration configuration, IApplicationConstants constants)
    {
        if (string.IsNullOrEmpty(constants.BuildPayload))
        {
            return;
        }

        var json = EmbeddedConfigDecryptor.Decrypt(
            constants.BuildPayload,
            constants.AppName,
            constants.Version,
            constants.AppTag);

        SetEmbeddedConfig(configuration, json);
    }

    public static JsonObject GetEmbeddedConfig(this IConfiguration configuration, params string[] path)
    {
        var config = configuration.EmbeddedConfig;
        JsonNode? current = config;
        foreach (var segment in path)
        {
            current = current?[segment];
            if (current is null) return new JsonObject();
        }
        return current?.AsObject() ?? new JsonObject();
    }

    public static void SetEmbeddedConfig(this IConfiguration configuration, JsonObject config, bool mergeExisting = false)
    {
        if (mergeExisting && configuration.TryGetRefValue(EmbeddedConfigKey, out _))
        {
            try
            {
                var existing = configuration.EmbeddedConfig;
                foreach (var prop in config)
                {
                    existing[prop.Key] = prop.Value?.DeepClone();
                }
                config = existing;
            }
            catch (InvalidOperationException)
            {
            }
        }
        configuration.EmbeddedConfig = config;
    }

    public static void SetEmbeddedConfig(this IConfiguration configuration, string jsonString, bool mergeExisting = false)
    {
        var parsed = JsonNode.Parse(jsonString)?.AsObject();
        if (parsed is null) return;
        SetEmbeddedConfig(configuration, parsed, mergeExisting);
    }
}

