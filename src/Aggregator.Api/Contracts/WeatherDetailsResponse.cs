namespace Aggregator.Api.Contracts;

/// <summary>Temperatures are in °C.</summary>
public sealed record WeatherDetailsResponse(string Location, double? MinTemperature, double? MaxTemperature, string Conditions) : ItemDetailsResponse;
