using System.Text.Json.Serialization;
using Application.Jobs.Models;

namespace Application.Serialization;

/// <summary>
/// Source-generated JSON context for Application types.
/// Add [JsonSerializable(typeof(T))] for each type that needs serialization.
/// Supports Native AOT compilation.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(GenerateRequest))]
[JsonSerializable(typeof(GenerateResponse))]
[JsonSerializable(typeof(JobResult))]
public partial class ApplicationJsonContext : JsonSerializerContext
{
}
