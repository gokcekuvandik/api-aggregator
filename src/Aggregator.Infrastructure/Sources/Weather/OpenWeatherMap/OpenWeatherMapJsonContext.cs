using System.Text.Json.Serialization;

namespace Aggregator.Infrastructure.Sources.Weather.OpenWeatherMap;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(OpenWeatherMapForecast))]
internal sealed partial class OpenWeatherMapJsonContext : JsonSerializerContext;
