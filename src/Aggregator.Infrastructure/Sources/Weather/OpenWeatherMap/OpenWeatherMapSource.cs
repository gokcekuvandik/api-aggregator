using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Serialization;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Sources.Weather.OpenWeatherMap;

internal sealed class OpenWeatherMapOptions : WeatherOptions
{
    public OpenWeatherMapOptions() => BaseAddress = new Uri("https://api.openweathermap.org/");

    [Required]
    [RegularExpression(HeaderSafePattern, ErrorMessage = HeaderSafeMessage)]
    public string? ApiKey { get; set; }
}

/// <summary>Three-hourly forecast from OpenWeatherMap; used instead of Open-Meteo when an API key is configured.</summary>
internal sealed class OpenWeatherMapSource(SourceFetcher fetcher, IOptions<OpenWeatherMapOptions> options) : IDataSource
{
    private const int ReadingsPerDay = 8;
    private const string UnknownConditions = "unknown conditions";

    public string Name => SourceNames.Weather;

    public IReadOnlyList<string> Categories { get; } = [SourceNames.Weather];

    public Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var path = string.Create(CultureInfo.InvariantCulture,
            $"data/2.5/forecast?lat={settings.Latitude:0.######}&lon={settings.Longitude:0.######}&units=metric&cnt={settings.ForecastDays * ReadingsPerDay}&appid={Uri.EscapeDataString(settings.ApiKey ?? string.Empty)}");

        return fetcher.FetchAsync(
            Name,
            new HttpRequestMessage(HttpMethod.Get, path),
            OpenWeatherMapJsonContext.Default.OpenWeatherMapForecast,
            forecast => WeatherItemMapper.ToItems(Name, settings.Location, forecast.List.Select(ToReading)),
            cancellationToken);
    }

    private static WeatherReading ToReading(OpenWeatherMapEntry entry) => new(
        At: DateTimeOffset.FromUnixTimeSeconds(entry.Dt),
        MinTemperature: entry.Main.TempMin,
        MaxTemperature: entry.Main.TempMax,
        Conditions: entry.Weather is [{ Description: { } description }, ..] ? description : UnknownConditions);
}

internal sealed record OpenWeatherMapForecast(IReadOnlyList<OpenWeatherMapEntry> List);

internal sealed record OpenWeatherMapEntry(long Dt, OpenWeatherMapMain Main, IReadOnlyList<OpenWeatherMapCondition>? Weather = null);

internal sealed record OpenWeatherMapMain(double? TempMin = null, double? TempMax = null);

internal sealed record OpenWeatherMapCondition(string? Description = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(OpenWeatherMapForecast))]
internal sealed partial class OpenWeatherMapJsonContext : JsonSerializerContext;
