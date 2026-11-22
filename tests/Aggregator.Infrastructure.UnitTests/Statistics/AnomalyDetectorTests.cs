using System.Diagnostics.Metrics;
using Aggregator.Application.Sources;
using Aggregator.Application.Statistics;
using Aggregator.Infrastructure.Statistics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Aggregator.Infrastructure.UnitTests.Statistics;

public sealed class AnomalyDetectorTests : IDisposable
{
    private static readonly UpstreamApi News = new("news", null);
    private static readonly UpstreamApi HackerNews = new("news", "hacker-news");
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly FakeLogger<AnomalyDetector> _logger = new();
    private readonly FakeTimeProvider _time = new();
    private readonly StubReader _reader = new();
    private readonly MetricCollector<long> _anomalies;
    private readonly AnomalyDetector _detector;

    public AnomalyDetectorTests()
    {
        var meters = _services.GetRequiredService<IMeterFactory>();
        _anomalies = new MetricCollector<long>(meters, SourceCacheMetrics.MeterName, AnomalyDetector.AnomaliesName);
        _detector = new AnomalyDetector(_reader, Options.Create(new StatisticsOptions()), _time, meters, _logger);
    }

    [Fact]
    public void CheckOnce_ShouldLogOneStructuredWarningAndCountIt_WhenAnApiBecomesAnomalous()
    {
        _reader.Set(Snapshot(News, overallMs: 100, windowMs: 200, windowCount: 12));

        _detector.CheckOnce();

        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.StructuredState.ShouldNotBeNull().ToDictionary(p => p.Key, p => p.Value).ShouldSatisfyAllConditions(
            state => state["Source"].ShouldBe("news"),
            state => state["Provider"].ShouldBe("primary"),
            state => state["WindowAverageMs"].ShouldBe("200"),
            state => state["OverallAverageMs"].ShouldBe("100"),
            state => state["Ratio"].ShouldBe("2"),
            state => state["SampleCount"].ShouldBe("12"));
        var anomaly = _anomalies.GetMeasurementSnapshot().ShouldHaveSingleItem();
        anomaly.Value.ShouldBe(1);
        anomaly.Tags.ShouldBe([new("source", "news"), new("provider", "primary")], ignoreOrder: true);
    }

    [Fact]
    public void CheckOnce_ShouldStayQuiet_WhenTheApiIsStillAnomalousOnLaterTicks()
    {
        _reader.Set(Snapshot(News, overallMs: 100, windowMs: 200, windowCount: 12));

        _detector.CheckOnce();
        _detector.CheckOnce();
        _detector.CheckOnce();

        _logger.Collector.Count.ShouldBe(1);
        _anomalies.GetMeasurementSnapshot().Count.ShouldBe(1);
    }

    [Fact]
    public void CheckOnce_ShouldLogOneRecoveryWithoutCounting_WhenAnAnomalousApiIsBackToNormal()
    {
        _reader.Set(Snapshot(News, overallMs: 100, windowMs: 200, windowCount: 12));
        _detector.CheckOnce();

        _reader.Set(Snapshot(News, overallMs: 100, windowMs: 110, windowCount: 12));
        _detector.CheckOnce();
        _detector.CheckOnce();

        var records = _logger.Collector.GetSnapshot();
        records.Count.ShouldBe(2);
        records[1].Level.ShouldBe(LogLevel.Information);
        records[1].StructuredState.ShouldNotBeNull().ShouldContain(new KeyValuePair<string, string?>("Source", "news"));
        _anomalies.GetMeasurementSnapshot().Count.ShouldBe(1);
    }

    [Fact]
    public void CheckOnce_ShouldCountASecondEpisode_WhenTheApiTurnsAnomalousAgainAfterRecovering()
    {
        _reader.Set(Snapshot(News, overallMs: 100, windowMs: 200, windowCount: 12));
        _detector.CheckOnce();
        _reader.Set(Snapshot(News, overallMs: 100, windowMs: 100, windowCount: 12));
        _detector.CheckOnce();
        _reader.Set(Snapshot(News, overallMs: 100, windowMs: 200, windowCount: 12));
        _detector.CheckOnce();

        _logger.Collector.Count.ShouldBe(3);
        _anomalies.GetMeasurementSnapshot().Count.ShouldBe(2);
    }

    [Fact]
    public void CheckOnce_ShouldNotReportAnything_WhenTheWindowHasTooFewSamples()
    {
        _reader.Set(Snapshot(News, overallMs: 100, windowMs: 5_000, windowCount: 9));

        _detector.CheckOnce();

        _logger.Collector.Count.ShouldBe(0);
        _anomalies.GetMeasurementSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void CheckOnce_ShouldKeepTheAnomaly_WhenTrafficDropsBelowTheMinimumSamples()
    {
        _reader.Set(Snapshot(News, overallMs: 100, windowMs: 200, windowCount: 12));
        _detector.CheckOnce();

        _reader.Set(Snapshot(News, overallMs: 100, windowMs: 0, windowCount: 0));
        _detector.CheckOnce();
        _reader.Set(Snapshot(News, overallMs: 100, windowMs: 200, windowCount: 12));
        _detector.CheckOnce();

        _logger.Collector.Count.ShouldBe(1);
        _anomalies.GetMeasurementSnapshot().Count.ShouldBe(1);
    }

    [Fact]
    public void CheckOnce_ShouldTrackEachApiOnItsOwn_WhenOnlyTheSecondaryIsAnomalous()
    {
        _reader.Set(
            Snapshot(News, overallMs: 100, windowMs: 100, windowCount: 50),
            Snapshot(HackerNews, overallMs: 40, windowMs: 90, windowCount: 20));

        _detector.CheckOnce();

        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.StructuredState.ShouldNotBeNull().ShouldContain(new KeyValuePair<string, string?>("Provider", "hacker-news"));
        _anomalies.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["provider"].ShouldBe("hacker-news");
    }

    [Fact]
    public void CheckOnce_ShouldReportAnInfiniteRatio_WhenTheOverallAverageIsZero()
    {
        _reader.Set(Snapshot(News, overallMs: 0, windowMs: 1, windowCount: 10));

        _detector.CheckOnce();

        _logger.Collector.GetSnapshot().ShouldHaveSingleItem().StructuredState.ShouldNotBeNull()
            .ShouldContain(new KeyValuePair<string, string?>("Ratio", "Infinity"));
    }

    [Fact]
    public void CheckOnce_ShouldApplyTheConfiguredRule_WhenAnomalySettingsAreCustom()
    {
        var options = new StatisticsOptions { Anomalies = new() { Threshold = 2, MinSamples = 3 } };
        var detector = new AnomalyDetector(_reader, Options.Create(options), _time, _services.GetRequiredService<IMeterFactory>(), _logger);
        _reader.Set(Snapshot(News, overallMs: 100, windowMs: 250, windowCount: 3), Snapshot(HackerNews, overallMs: 100, windowMs: 301, windowCount: 3));

        detector.CheckOnce();

        _logger.Collector.GetSnapshot().ShouldHaveSingleItem().StructuredState.ShouldNotBeNull()
            .ShouldContain(new KeyValuePair<string, string?>("Provider", "hacker-news"));
    }

    [Fact]
    public async Task ExecuteAsync_ShouldCheckOncePerInterval_WhenFakeTimeAdvances()
    {
        _reader.Set(Snapshot(News, overallMs: 100, windowMs: 200, windowCount: 12));
        await _detector.StartAsync(TestContext.Current.CancellationToken);

        _reader.Calls.ShouldBe(0);
        _time.Advance(Interval);
        await _reader.WaitForCallsAsync(1);
        _time.Advance(Interval);
        await _reader.WaitForCallsAsync(2);

        await _detector.StopAsync(TestContext.Current.CancellationToken);
        _logger.Collector.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldLogTheErrorAndKeepChecking_WhenAReadThrows()
    {
        _reader.FailNext(new InvalidOperationException("boom"));
        _reader.Set(Snapshot(News, overallMs: 100, windowMs: 200, windowCount: 12));
        await _detector.StartAsync(TestContext.Current.CancellationToken);

        _time.Advance(Interval);
        await _reader.WaitForCallsAsync(1);
        _time.Advance(Interval);
        await _reader.WaitForCallsAsync(2);
        await _detector.StopAsync(TestContext.Current.CancellationToken);

        var records = _logger.Collector.GetSnapshot();
        records.ShouldContain(r => r.Level == LogLevel.Error && r.Exception is InvalidOperationException);
        records.ShouldContain(r => r.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task StopAsync_ShouldEndTheLoopCleanly_WhenTheHostStops()
    {
        await _detector.StartAsync(TestContext.Current.CancellationToken);

        await _detector.StopAsync(TestContext.Current.CancellationToken);

        _detector.ExecuteTask.ShouldNotBeNull().IsCompletedSuccessfully.ShouldBeTrue();
        _logger.Collector.Count.ShouldBe(0);
    }

    public void Dispose()
    {
        _detector.Dispose();
        _anomalies.Dispose();
        _services.Dispose();
    }

    private static ApiStatisticsSnapshot Snapshot(UpstreamApi api, double overallMs, double windowMs, long windowCount) =>
        new(
            api,
            TotalRequests: 1_000,
            AverageDuration: TimeSpan.FromMilliseconds(overallMs),
            Performance: null,
            Fast: BucketStatistics.Empty,
            Average: BucketStatistics.Empty,
            Slow: BucketStatistics.Empty,
            Retries: 0,
            Failures: 0,
            Cancelled: 0,
            Rejected: 0,
            CacheHits: 0,
            Recent: new WindowStatistics(TimeSpan.FromMinutes(5), windowCount, TimeSpan.FromMilliseconds(windowMs)));

    /// <summary>Returns the snapshots a test set, can fail once, and lets a test wait until the detector has read a given number of times.</summary>
    private sealed class StubReader : IStatisticsReader
    {
        private readonly Lock _lock = new();
        private IReadOnlyList<ApiStatisticsSnapshot> _snapshots = [];
        private Exception? _failure;
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public void Set(params ApiStatisticsSnapshot[] snapshots)
        {
            lock (_lock)
            {
                _snapshots = snapshots;
            }
        }

        public void FailNext(Exception failure)
        {
            lock (_lock)
            {
                _failure = failure;
            }
        }

        public IReadOnlyList<ApiStatisticsSnapshot> GetSnapshots()
        {
            lock (_lock)
            {
                _calls++;
                var failure = _failure;
                _failure = null;
                return failure is null ? _snapshots : throw failure;
            }
        }

        public async Task WaitForCallsAsync(int calls)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (Calls < calls)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(5), timeout.Token);
            }
        }
    }
}
