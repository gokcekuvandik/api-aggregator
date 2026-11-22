namespace Aggregator.Domain.Items;

/// <summary>A forecast reading; temperatures are in °C and may be missing.</summary>
public sealed record WeatherDetails(string Location, double? MinTemperature, double? MaxTemperature, string Conditions) : ItemDetails;
