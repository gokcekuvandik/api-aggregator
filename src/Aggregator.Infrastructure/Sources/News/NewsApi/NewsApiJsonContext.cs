using System.Text.Json.Serialization;

namespace Aggregator.Infrastructure.Sources.News.NewsApi;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(NewsApiResponse))]
internal sealed partial class NewsApiJsonContext : JsonSerializerContext;
