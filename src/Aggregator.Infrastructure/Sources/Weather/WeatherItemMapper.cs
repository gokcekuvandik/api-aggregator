using System.Globalization;
using Aggregator.Domain.Items;

namespace Aggregator.Infrastructure.Sources.Weather;

/// <summary>One forecast reading, already in provider-neutral form.</summary>
internal sealed record WeatherReading(DateTimeOffset At, double? MinTemperature, double? MaxTemperature, string Conditions);

/// <summary>Turns weather readings from either provider into items.</summary>
internal static class WeatherItemMapper
{
    public static IReadOnlyList<AggregatedItem> ToItems(string sourceName, string location, IEnumerable<WeatherReading> readings) =>
        [.. readings.Select(reading => new AggregatedItem(
            Id: string.Create(CultureInfo.InvariantCulture, $"{sourceName}:{location}:{reading.At.UtcDateTime:yyyy-MM-ddTHH:mm}Z"),
            Source: sourceName,
            Category: SourceNames.Weather,
            Title: $"{location}: {reading.Conditions}{Temperatures(reading.MinTemperature, reading.MaxTemperature)}",
            Summary: null,
            Url: null,
            PublishedAt: reading.At))];

    private static string Temperatures(double? min, double? max) => (min, max) switch
    {
        ({ } low, { } high) => $", {Degrees(low)} to {Degrees(high)} °C",
        ({ } low, null) => $", min {Degrees(low)} °C",
        (null, { } high) => $", max {Degrees(high)} °C",
        _ => string.Empty,
    };

    private static string Degrees(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.AwayFromZero);
        return (rounded == 0 ? 0 : rounded).ToString("0", CultureInfo.InvariantCulture);
    }
}
