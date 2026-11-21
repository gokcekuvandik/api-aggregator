
namespace Aggregator.Infrastructure.Sources.Caching;

/// <summary>Cache settings for one source, bound from <c>Sources:{name}:Cache</c>; options are named after the source and read once at startup.</summary>
internal sealed class SourceCacheOptions
{
    public const string SectionName = "Cache";

    internal static readonly TimeSpan MinTtl = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan MaxTtl = TimeSpan.FromHours(1);
    internal static readonly TimeSpan MinLastKnownGoodTtl = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan MaxLastKnownGoodTtl = TimeSpan.FromDays(7);

    public bool Enabled { get; set; } = true;

    /// <summary>How long an answer is served from the cache before the source is asked again.</summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long an answer is kept to stand in for the source when every provider fails.</summary>
    public TimeSpan LastKnownGoodTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Weather changes slowly, news quickly; GitHub's anonymous rate limit favours a longer time to live.</summary>
    internal static TimeSpan DefaultTtlFor(string sourceName) => sourceName switch
    {
        SourceNames.Weather => TimeSpan.FromMinutes(10),
        SourceNames.Repositories => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromMinutes(5),
    };
}
