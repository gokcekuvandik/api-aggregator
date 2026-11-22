namespace Aggregator.Infrastructure.Sources.Weather;

/// <summary>One forecast reading, already in provider-neutral form.</summary>
internal sealed record WeatherReading(DateTimeOffset At, double? MinTemperature, double? MaxTemperature, string Conditions);
