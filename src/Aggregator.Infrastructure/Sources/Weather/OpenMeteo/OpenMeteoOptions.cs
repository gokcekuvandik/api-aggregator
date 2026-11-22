namespace Aggregator.Infrastructure.Sources.Weather.OpenMeteo;

internal sealed class OpenMeteoOptions : WeatherOptions
{
    public OpenMeteoOptions() => BaseAddress = new Uri("https://api.open-meteo.com/");

    /// <summary>Open-Meteo's keyless geocoding API, used to find a requested city.</summary>
    public Uri? GeocodingAddress { get; set; } = new("https://geocoding-api.open-meteo.com/");

    internal override IEnumerable<(string Key, Uri? Address)> Addresses =>
        [.. base.Addresses, (nameof(GeocodingAddress), GeocodingAddress)];
}
