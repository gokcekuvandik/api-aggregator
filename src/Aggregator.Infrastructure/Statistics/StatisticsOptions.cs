using Aggregator.Domain.Statistics;

namespace Aggregator.Infrastructure.Statistics;

/// <summary>Statistics settings, bound from <c>Statistics</c> and validated at startup.</summary>
internal sealed class StatisticsOptions
{
    public const string SectionName = "Statistics";

    public BucketOptions Buckets { get; set; } = new();

    public PerformanceThresholds Thresholds => new(Buckets.FastBelow, Buckets.SlowAbove);

    /// <summary>Requests shorter than <see cref="FastBelow"/> are fast, longer than <see cref="SlowAbove"/> slow, and the rest average.</summary>
    internal sealed class BucketOptions
    {
        public TimeSpan FastBelow { get; set; } = PerformanceThresholds.Default.FastBelow;

        public TimeSpan SlowAbove { get; set; } = PerformanceThresholds.Default.SlowAbove;
    }
}
