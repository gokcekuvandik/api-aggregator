using System.ComponentModel.DataAnnotations;

namespace Aggregator.Infrastructure.Sources.Weather.OpenWeatherMap;

internal sealed class OpenWeatherMapOptions : WeatherOptions
{
    public OpenWeatherMapOptions() => BaseAddress = new Uri("https://api.openweathermap.org/");

    [Required]
    [RegularExpression(HeaderSafePattern, ErrorMessage = OptionsValidationMessages.HeaderSafe)]
    public string? ApiKey { get; set; }
}
