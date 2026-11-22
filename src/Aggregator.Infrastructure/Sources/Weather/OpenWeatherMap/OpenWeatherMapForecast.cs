namespace Aggregator.Infrastructure.Sources.Weather.OpenWeatherMap;

internal sealed record OpenWeatherMapForecast(IReadOnlyList<OpenWeatherMapEntry> List, OpenWeatherMapCity? City = null);
