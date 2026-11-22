namespace Aggregator.Infrastructure.Sources.Weather.OpenMeteo;

/// <summary>A geocoding answer; <c>results</c> is left out entirely when nothing matches.</summary>
internal sealed record OpenMeteoGeocoding(IReadOnlyList<OpenMeteoPlace>? Results = null);
