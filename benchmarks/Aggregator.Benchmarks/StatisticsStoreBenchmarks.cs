using System.Threading.Channels;
using Aggregator.Application.Statistics;
using Aggregator.Domain.Statistics;
using Aggregator.Infrastructure.Statistics;
using BenchmarkDotNet.Attributes;

namespace Aggregator.Benchmarks;

/// <summary>
/// Records 10,000 requests split across parallel writers, then reads one snapshot, for three ways of keeping per-API statistics.
/// <see cref="Lock"/> is the store the service uses; the other two exist only here for comparison.
/// </summary>
public class StatisticsStoreBenchmarks
{
    private const int Records = 10_000;
    private static readonly UpstreamApi Api = new("news", null);
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Params(1, 4, 16)]
    public int Writers { get; set; }

    [Benchmark(Baseline = true, Description = "Lock (store in use)")]
    public long Lock()
    {
        var statistics = new ApiStatistics(Api, PerformanceThresholds.Default);
        Parallel.For(0, Writers, writer => ForEachRecord(writer, duration => statistics.RecordRequest(duration, RequestOutcome.Success, 1, Now)));
        return statistics.Snapshot(Now).TotalRequests;
    }

    [Benchmark(Description = "Interlocked (no consistent snapshot)")]
    public long Interlocked()
    {
        var statistics = new InterlockedStatistics(PerformanceThresholds.Default);
        Parallel.For(0, Writers, writer => ForEachRecord(writer, statistics.Record));
        return statistics.Total();
    }

    [Benchmark(Description = "Channel (single-writer actor)")]
    public async Task<long> Channel()
    {
        var statistics = new ChannelStatistics(PerformanceThresholds.Default);
        Parallel.For(0, Writers, writer => ForEachRecord(writer, statistics.Record));
        return await statistics.DrainAsync();
    }

    private void ForEachRecord(int writer, Action<TimeSpan> record)
    {
        var perWriter = Records / Writers;
        for (var i = 0; i < perWriter; i++)
        {
            record(TimeSpan.FromMilliseconds((writer * perWriter + i) % 300));
        }
    }

    /// <summary>Bucket counters updated with atomic adds; a reader can see a count without its matching total.</summary>
    private sealed class InterlockedStatistics(PerformanceThresholds thresholds)
    {
        private readonly long[] _counts = new long[3];
        private readonly long[] _ticks = new long[3];

        public void Record(TimeSpan duration)
        {
            var bucket = (int)thresholds.Classify(duration);
            System.Threading.Interlocked.Increment(ref _counts[bucket]);
            System.Threading.Interlocked.Add(ref _ticks[bucket], duration.Ticks);
        }

        public long Total() => _counts.Sum();
    }

    /// <summary>Writers post measurements to an unbounded channel and one reader applies them to plain fields.</summary>
    private sealed class ChannelStatistics
    {
        private readonly Channel<TimeSpan> _channel = System.Threading.Channels.Channel.CreateUnbounded<TimeSpan>(new UnboundedChannelOptions { SingleReader = true });
        private readonly long[] _counts = new long[3];
        private readonly long[] _ticks = new long[3];
        private readonly Task _reader;

        public ChannelStatistics(PerformanceThresholds thresholds) => _reader = ReadAsync(thresholds);

        public void Record(TimeSpan duration) => _channel.Writer.TryWrite(duration);

        public async Task<long> DrainAsync()
        {
            _channel.Writer.Complete();
            await _reader;
            return _counts.Sum();
        }

        private async Task ReadAsync(PerformanceThresholds thresholds)
        {
            await foreach (var duration in _channel.Reader.ReadAllAsync())
            {
                var bucket = (int)thresholds.Classify(duration);
                _counts[bucket]++;
                _ticks[bucket] += duration.Ticks;
            }
        }
    }
}
