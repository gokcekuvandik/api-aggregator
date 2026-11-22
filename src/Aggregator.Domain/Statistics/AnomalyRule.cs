namespace Aggregator.Domain.Statistics;

/// <summary>
/// Flags an external API whose recent average is more than <see cref="Threshold"/> above its overall average, once the window holds at least <see cref="MinSamples"/> requests.
/// The defaults are 0.5 (50% slower) and 10 samples.
/// </summary>
public sealed record AnomalyRule
{
    public AnomalyRule(double threshold, int minSamples)
    {
        if (!double.IsFinite(threshold))
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), threshold, "The threshold must be a finite number.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(threshold, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(minSamples, 1);

        Threshold = threshold;
        MinSamples = minSamples;
    }

    public static AnomalyRule Default { get; } = new(0.5, 10);

    public double Threshold { get; }

    public int MinSamples { get; }

    public AnomalyVerdict Evaluate(TimeSpan windowAverage, TimeSpan overallAverage, long windowCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(windowAverage, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(overallAverage, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(windowCount);

        if (windowCount < MinSamples)
        {
            return AnomalyVerdict.InsufficientSamples;
        }

        return windowAverage.Ticks > overallAverage.Ticks * (1 + Threshold) ? AnomalyVerdict.Anomalous : AnomalyVerdict.Normal;
    }
}
