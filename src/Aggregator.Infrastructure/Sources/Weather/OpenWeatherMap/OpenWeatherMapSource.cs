using System.Globalization;
using System.Net;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Aggregator.Infrastructure.Sources.Relevance;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Sources.Weather.OpenWeatherMap;

/// <summary>
/// Three-hourly forecast from OpenWeatherMap; used instead of Open-Meteo when an API key is configured.
/// A requested city is passed by name, and the answer's own city name labels the items; a 404 for it means no such city and yields no items.
/// </summary>
internal sealed class OpenWeatherMapSource(SourceFetcher fetcher, IOptions<OpenWeatherMapOptions> options, IRelevanceScorer<DateTimeOffset> scorer) : IDataSource
{
    private const int ReadingsPerDay = 8;
    private const string UnknownConditions = "unknown conditions";

    public string Name => SourceNames.Weather;

    public IReadOnlyList<string> Categories { get; } = [SourceNames.Weather];

    public SearchFields UsedFields => SearchFields.City | SearchFields.From;

    public Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var settings = options.Value;
        var city = query.City;
        var where = city is null
            ? string.Create(CultureInfo.InvariantCulture, $"lat={settings.Latitude:0.######}&lon={settings.Longitude:0.######}")
            : $"q={Uri.EscapeDataString(city)}";
        var path = string.Create(CultureInfo.InvariantCulture,
            $"data/2.5/forecast?{where}&units=metric&cnt={settings.ForecastDays * ReadingsPerDay}&appid={Uri.EscapeDataString(settings.ApiKey ?? string.Empty)}");

        return fetcher.FetchAsync(
            settings.ClientFor(Name),
            new HttpRequestMessage(HttpMethod.Get, path),
            OpenWeatherMapJsonContext.Default.OpenWeatherMapForecast,
            forecast => WeatherItemMapper.ToItems(Name, Location(forecast, city, settings), forecast.List.Select(ToReading), scorer, query),
            city is null ? null : HttpStatusCode.NotFound,
            cancellationToken);
    }

    private static string Location(OpenWeatherMapForecast forecast, string? city, OpenWeatherMapOptions settings) =>
        city is null ? settings.Location : forecast.City?.Name ?? city;

    private static WeatherReading ToReading(OpenWeatherMapEntry entry) => new(
        At: DateTimeOffset.FromUnixTimeSeconds(entry.Dt),
        MinTemperature: entry.Main.TempMin,
        MaxTemperature: entry.Main.TempMax,
        Conditions: entry.Weather is [{ Description: { } description }, ..] ? description : UnknownConditions);
}
