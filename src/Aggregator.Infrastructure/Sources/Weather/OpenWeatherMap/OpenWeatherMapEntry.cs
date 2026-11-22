namespace Aggregator.Infrastructure.Sources.Weather.OpenWeatherMap;

internal sealed record OpenWeatherMapEntry(long Dt, OpenWeatherMapMain Main, IReadOnlyList<OpenWeatherMapCondition>? Weather = null);
