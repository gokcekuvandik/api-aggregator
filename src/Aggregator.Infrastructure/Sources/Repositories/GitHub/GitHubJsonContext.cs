using System.Text.Json.Serialization;

namespace Aggregator.Infrastructure.Sources.Repositories.GitHub;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(GitHubSearch))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext;
