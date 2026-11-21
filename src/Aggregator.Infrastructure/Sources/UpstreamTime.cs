using System.Globalization;

namespace Aggregator.Infrastructure.Sources;

/// <summary>
/// Formats date bounds for upstreams that only accept whole seconds.
/// The start is rounded down and the end up, so a pushed-down range never drops an item the in-memory filter would keep.
/// </summary>
internal static class UpstreamTime
{
    public static long StartUnixSeconds(DateTimeOffset start) => start.ToUnixTimeSeconds();

    public static long EndUnixSeconds(DateTimeOffset end) => CeilingToSecond(end).ToUnixTimeSeconds();

    public static string StartIso(DateTimeOffset start) => Iso(start);

    public static string EndIso(DateTimeOffset end) => Iso(CeilingToSecond(end));

    private static DateTimeOffset CeilingToSecond(DateTimeOffset value)
    {
        var remainder = value.UtcTicks % TimeSpan.TicksPerSecond;
        if (remainder == 0)
        {
            return value;
        }

        // The last second of year 9999 cannot be rounded up, so it is cut down instead of overflowing.
        var step = TimeSpan.TicksPerSecond - remainder;
        return value.UtcTicks > DateTimeOffset.MaxValue.UtcTicks - step ? value.AddTicks(-remainder) : value.AddTicks(step);
    }

    private static string Iso(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
