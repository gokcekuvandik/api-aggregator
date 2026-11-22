using Aggregator.Domain.Statistics;

namespace Aggregator.Infrastructure.Statistics;

/// <summary>Statistics settings, bound from <c>Statistics</c> and validated at startup.</summary>
internal sealed class StatisticsOptions
{
    public const string SectionName = "Statistics";

    public BucketOptions Buckets { get; set; } = new();

    public AnomalyOptions Anomalies { get; set; } = new();

    public PerformanceThresholds Thresholds => new(Buckets.FastBelow, Buckets.SlowAbove);

    /// <summary>How often the anomaly detector checks the statistics, and the rule it applies to each external API.</summary>
    internal sealed class AnomalyOptions
    {
        public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

        public double Threshold { get; set; } = AnomalyRule.Default.Threshold;

        public int MinSamples { get; set; } = AnomalyRule.Default.MinSamples;

        public AnomalyRule Rule => new(Threshold, MinSamples);
    }

    /// <summary>Requests shorter than <see cref="FastBelow"/> are fast, longer than <see cref="SlowAbove"/> slow, and the rest average.</summary>
    internal sealed class BucketOptions
    {
        public TimeSpan FastBelow { get; set; } = PerformanceThresholds.Default.FastBelow;

        public TimeSpan SlowAbove { get; set; } = PerformanceThresholds.Default.SlowAbove;
    }
}
