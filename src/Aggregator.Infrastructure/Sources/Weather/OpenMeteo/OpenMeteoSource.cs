using System.Globalization;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Aggregator.Infrastructure.Sources.Relevance;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Sources.Weather.OpenMeteo;

internal sealed class OpenMeteoOptions : WeatherOptions
{
    public OpenMeteoOptions() => BaseAddress = new Uri("https://api.open-meteo.com/");

    /// <summary>Open-Meteo's keyless geocoding API, used to find a requested city.</summary>
    public Uri? GeocodingAddress { get; set; } = new("https://geocoding-api.open-meteo.com/");

    internal override IEnumerable<(string Key, Uri? Address)> Addresses =>
        [.. base.Addresses, (nameof(GeocodingAddress), GeocodingAddress)];
}

/// <summary>
/// Daily forecast from Open-Meteo, which needs no API key.
/// A requested city is first looked up with the geocoding API on the same client, so its failures count against this source.
/// A city with no match yields no items rather than a failure, because the upstream worked.
/// </summary>
internal sealed class OpenMeteoSource(SourceFetcher fetcher, IOptions<OpenMeteoOptions> options, IRelevanceScorer<DateTimeOffset> scorer) : IDataSource
{
    public string Name => SourceNames.Weather;

    public IReadOnlyList<string> Categories { get; } = [SourceNames.Weather];

    public async Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var settings = options.Value;
        var place = new OpenMeteoPlace(settings.Location, settings.Latitude, settings.Longitude);

        if (query.City is not null)
        {
            var (geocoding, failure) = await fetcher.ReadAsync(settings.ClientFor(Name), GeocodingRequest(settings, query.City), OpenMeteoJsonContext.Default.OpenMeteoGeocoding, cancellationToken);
            if (failure is not null)
            {
                return failure;
            }

            if (geocoding?.Results is not [var found, ..])
            {
                return new SourceSuccess([]);
            }

            place = found;
        }

        return await fetcher.FetchAsync(
            settings.ClientFor(Name),
            ForecastRequest(settings, place),
            OpenMeteoJsonContext.Default.OpenMeteoForecast,
            forecast => WeatherItemMapper.ToItems(Name, place.Name, Readings(forecast.Daily), scorer, query),
            cancellationToken);
    }

    private static HttpRequestMessage GeocodingRequest(OpenMeteoOptions settings, string city) =>
        new(HttpMethod.Get, new Uri(settings.GeocodingAddress!, $"v1/search?name={Uri.EscapeDataString(city)}&count=1&language=en&format=json"));

    private static HttpRequestMessage ForecastRequest(OpenMeteoOptions settings, OpenMeteoPlace place) =>
        new(HttpMethod.Get, string.Create(CultureInfo.InvariantCulture,
            $"v1/forecast?latitude={place.Latitude:0.######}&longitude={place.Longitude:0.######}&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=UTC&forecast_days={settings.ForecastDays}"));

    private static IEnumerable<WeatherReading> Readings(OpenMeteoDaily daily) =>
        daily.Time.Select((date, i) => new WeatherReading(
            At: new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            MinTemperature: daily.TemperatureMin.ElementAtOrDefault(i),
            MaxTemperature: daily.TemperatureMax.ElementAtOrDefault(i),
            Conditions: WmoWeatherCodes.Describe(daily.WeatherCode.ElementAtOrDefault(i))));
}
