using Aggregator.Application.Statistics;
using Aggregator.Domain.Statistics;

namespace Aggregator.Infrastructure.Statistics;

/// <summary>
/// Lifetime totals per bucket plus a ring of 10-second slices covering the last five minutes, for one external API.
/// Every write and every snapshot copy holds the same lock for a few field updates, so a snapshot is never torn.
/// </summary>
internal sealed class ApiStatistics(UpstreamApi api, PerformanceThresholds thresholds)
{
    private const int SliceCount = 30;
    private static readonly TimeSpan SliceLength = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan WindowLength = SliceLength * SliceCount;
    private static readonly int BucketCount = Enum.GetValues<PerformanceBucket>().Length;

    private readonly Lock _lock = new();
    private readonly Tally[] _buckets = new Tally[BucketCount];
    private readonly Slice[] _slices = new Slice[SliceCount];
    private long _retries;
    private long _failures;
    private long _cancelled;
    private long _rejected;
    private long _cacheHits;

    public void RecordRequest(TimeSpan duration, RequestOutcome outcome, int attempts, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempts);
        var bucket = (int)thresholds.Classify(duration);
        var sliceIndex = SliceIndex(now);

        lock (_lock)
        {
            _retries += Math.Max(0, attempts - 1);
            switch (outcome)
            {
                case RequestOutcome.Cancelled:
                    _cancelled++;
                    return;
                case RequestOutcome.Rejected:
                    _rejected++;
                    return;
                case RequestOutcome.Failure:
                    _failures++;
                    break;
            }

            _buckets[bucket] = _buckets[bucket].Add(duration);

            var position = (int)(sliceIndex % SliceCount);
            var slice = _slices[position];
            if (slice.Index <= sliceIndex)
            {
                _slices[position] = new Slice(sliceIndex, (slice.Index == sliceIndex ? slice.Tally : default).Add(duration));
            }
        }
    }

    public void RecordCacheHit()
    {
        lock (_lock)
        {
            _cacheHits++;
        }
    }

    public ApiStatisticsSnapshot Snapshot(DateTimeOffset now)
    {
        var buckets = new Tally[BucketCount];
        Tally recent = default;
        long retries, failures, cancelled, rejected, cacheHits;
        var oldestSlice = SliceIndex(now) - SliceCount + 1;

        lock (_lock)
        {
            _buckets.CopyTo(buckets, 0);
            (retries, failures, cancelled, rejected, cacheHits) = (_retries, _failures, _cancelled, _rejected, _cacheHits);
            foreach (var slice in _slices)
            {
                if (slice.Index >= oldestSlice)
                {
                    recent = recent.Add(slice.Tally);
                }
            }
        }

        var total = buckets.Aggregate(default(Tally), (sum, tally) => sum.Add(tally));
        return new ApiStatisticsSnapshot(
            api,
            total.Count,
            total.Average,
            total.Count == 0 ? null : thresholds.Classify(total.Average),
            buckets[(int)PerformanceBucket.Fast].ToBucket(),
            buckets[(int)PerformanceBucket.Average].ToBucket(),
            buckets[(int)PerformanceBucket.Slow].ToBucket(),
            retries,
            failures,
            cancelled,
            rejected,
            cacheHits,
            new WindowStatistics(WindowLength, recent.Count, recent.Average));
    }

    private static long SliceIndex(DateTimeOffset now) => now.UtcTicks / SliceLength.Ticks;

    /// <summary>A count of timed requests and their summed duration in ticks.</summary>
    private readonly record struct Tally(long Count, long Ticks)
    {
        public TimeSpan Average => Count == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(Ticks / Count);

        public Tally Add(TimeSpan duration) => new(Count + 1, Ticks + duration.Ticks);

        public Tally Add(Tally other) => new(Count + other.Count, Ticks + other.Ticks);

        public BucketStatistics ToBucket() => new(Count, Average);
    }

    /// <summary>One 10-second slice; <see cref="Index"/> is the slice number since year one, so a stale or newer slot is recognised.</summary>
    private readonly record struct Slice(long Index, Tally Tally);
}
