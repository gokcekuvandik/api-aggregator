using System.Globalization;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Sources.Weather.OpenMeteo;

internal sealed class OpenMeteoOptions : WeatherOptions
{
    public OpenMeteoOptions() => BaseAddress = new Uri("https://api.open-meteo.com/");
}

/// <summary>Daily forecast from Open-Meteo, which needs no API key.</summary>
internal sealed class OpenMeteoSource(SourceFetcher fetcher, IOptions<OpenMeteoOptions> options) : IDataSource
{
    public string Name => SourceNames.Weather;

    public IReadOnlyList<string> Categories { get; } = [SourceNames.Weather];

    public Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var path = string.Create(CultureInfo.InvariantCulture,
            $"v1/forecast?latitude={settings.Latitude:0.######}&longitude={settings.Longitude:0.######}&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=UTC&forecast_days={settings.ForecastDays}");

        return fetcher.FetchAsync(
            Name,
            new HttpRequestMessage(HttpMethod.Get, path),
            OpenMeteoJsonContext.Default.OpenMeteoForecast,
            forecast => WeatherItemMapper.ToItems(Name, settings.Location, Readings(forecast.Daily)),
            cancellationToken);
    }

    private static IEnumerable<WeatherReading> Readings(OpenMeteoDaily daily) =>
        daily.Time.Select((date, i) => new WeatherReading(
            At: new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            MinTemperature: daily.TemperatureMin.ElementAtOrDefault(i),
            MaxTemperature: daily.TemperatureMax.ElementAtOrDefault(i),
            Conditions: WmoWeatherCodes.Describe(daily.WeatherCode.ElementAtOrDefault(i))));
}
