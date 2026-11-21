using System.Text.Json.Serialization;

namespace Aggregator.Infrastructure.Sources.Weather.OpenMeteo;

internal sealed record OpenMeteoForecast(OpenMeteoDaily Daily);

/// <summary>Parallel arrays, one entry per day.</summary>
internal sealed record OpenMeteoDaily(
    IReadOnlyList<DateOnly> Time,
    [property: JsonPropertyName("weather_code")] IReadOnlyList<int?> WeatherCode,
    [property: JsonPropertyName("temperature_2m_max")] IReadOnlyList<double?> TemperatureMax,
    [property: JsonPropertyName("temperature_2m_min")] IReadOnlyList<double?> TemperatureMin);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(OpenMeteoForecast))]
internal sealed partial class OpenMeteoJsonContext : JsonSerializerContext;
