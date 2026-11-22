using System.Text.Json.Serialization;

namespace Aggregator.Infrastructure.Sources.Weather.OpenMeteo;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(OpenMeteoForecast))]
[JsonSerializable(typeof(OpenMeteoGeocoding))]
internal sealed partial class OpenMeteoJsonContext : JsonSerializerContext;
