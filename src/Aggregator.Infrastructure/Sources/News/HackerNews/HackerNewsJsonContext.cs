using System.Text.Json.Serialization;

namespace Aggregator.Infrastructure.Sources.News.HackerNews;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(HackerNewsSearch))]
internal sealed partial class HackerNewsJsonContext : JsonSerializerContext;
