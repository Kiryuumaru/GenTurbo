using System.Text.Json.Serialization;
using Domain.Jobs.Entities;
using Domain.Jobs.Events;

namespace Domain.Serialization;

/// <summary>
/// Source-generated JSON context for Domain types.
/// Add [JsonSerializable(typeof(T))] for each type that needs serialization.
/// Supports Native AOT compilation.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(JobEntity))]
[JsonSerializable(typeof(JobCreatedEvent))]
[JsonSerializable(typeof(JobCompletedEvent))]
public partial class DomainJsonContext : JsonSerializerContext
{
}
