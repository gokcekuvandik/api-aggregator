using System.ComponentModel.DataAnnotations;

namespace Aggregator.Infrastructure.Sources.Weather;

/// <summary>Where and how far ahead to forecast; shared by both weather providers.</summary>
internal abstract class WeatherOptions : DataSourceOptions
{
    [Required]
    public string Location { get; set; } = "Athens";

    [Range(-90, 90)]
    public double Latitude { get; set; } = 37.98;

    [Range(-180, 180)]
    public double Longitude { get; set; } = 23.73;

    [Range(1, 5)]
    public int ForecastDays { get; set; } = 3;
}
