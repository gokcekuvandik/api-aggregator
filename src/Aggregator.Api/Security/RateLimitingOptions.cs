namespace Aggregator.Api.Security;

/// <summary>The token bucket each caller gets on <c>/api/v1</c>, bound from <c>RateLimiting</c> and validated at startup.</summary>
internal sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>A generous ceiling, so a typo such as hours for seconds fails validation instead of silently locking callers out.</summary>
    private static readonly TimeSpan MaxReplenishmentPeriod = TimeSpan.FromHours(1);

    /// <summary>The burst a caller may send at once.</summary>
    public int TokenLimit { get; set; } = 20;

    public int TokensPerPeriod { get; set; } = 10;

    public TimeSpan ReplenishmentPeriod { get; set; } = TimeSpan.FromSeconds(1);

    public bool IsValid =>
        TokenLimit >= 1
        && TokensPerPeriod >= 1
        && ReplenishmentPeriod > TimeSpan.Zero
        && ReplenishmentPeriod <= MaxReplenishmentPeriod;
}
