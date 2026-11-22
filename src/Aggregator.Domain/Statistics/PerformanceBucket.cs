namespace Aggregator.Domain.Statistics;

/// <summary>How fast an upstream request was, judged against <see cref="PerformanceThresholds"/>.</summary>
public enum PerformanceBucket
{
    Fast,
    Average,
    Slow,
}
