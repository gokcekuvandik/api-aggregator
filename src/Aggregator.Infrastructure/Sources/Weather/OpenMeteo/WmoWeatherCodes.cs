namespace Aggregator.Infrastructure.Sources.Weather.OpenMeteo;

/// <summary>Plain-language names for the WMO weather interpretation codes Open-Meteo returns.</summary>
internal static class WmoWeatherCodes
{
    public static string Describe(int? code) => code switch
    {
        0 => "clear sky",
        1 or 2 => "partly cloudy",
        3 => "overcast",
        45 or 48 => "fog",
        >= 51 and <= 57 => "drizzle",
        >= 61 and <= 67 => "rain",
        >= 71 and <= 77 => "snow",
        >= 80 and <= 82 => "rain showers",
        85 or 86 => "snow showers",
        >= 95 and <= 99 => "thunderstorm",
        _ => "unknown conditions",
    };
}
