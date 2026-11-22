using Aggregator.Application.Statistics;
using Aggregator.Domain.Statistics;
using Aggregator.Infrastructure.Statistics;
using Aggregator.Infrastructure.UnitTests.TestDoubles;

namespace Aggregator.Infrastructure.UnitTests.Statistics;

public sealed class StatisticsStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly UpstreamApi News = new("news", null);
    private static readonly UpstreamApi HackerNews = new("news", "hacker-news");
    private static readonly UpstreamApi Weather = new("weather", null);

    private readonly ManualTimeProvider _clock = new(Now);

    [Fact]
    public void GetSnapshots_ShouldBeEmpty_WhenNothingWasRecorded()
    {
        Store().GetSnapshots().ShouldBeEmpty();
    }

    [Fact]
    public void GetSnapshots_ShouldSplitRequestsIntoBuckets_WhenDurationsDiffer()
    {
        var store = Store();

        Record(store, News, 50, 150, 250, 300);

        var snapshot = store.GetSnapshots().ShouldHaveSingleItem();
        snapshot.Api.ShouldBe(News);
        snapshot.TotalRequests.ShouldBe(4);
        snapshot.AverageDuration.ShouldBe(TimeSpan.FromMilliseconds(187.5));
        snapshot.Performance.ShouldBe(PerformanceBucket.Average);
        snapshot.Fast.ShouldBe(new BucketStatistics(1, TimeSpan.FromMilliseconds(50)));
        snapshot.Average.ShouldBe(new BucketStatistics(1, TimeSpan.FromMilliseconds(150)));
        snapshot.Slow.ShouldBe(new BucketStatistics(2, TimeSpan.FromMilliseconds(275)));
    }

    [Theory]
    [InlineData(99, PerformanceBucket.Fast)]
    [InlineData(100, PerformanceBucket.Average)]
    [InlineData(200, PerformanceBucket.Average)]
    [InlineData(201, PerformanceBucket.Slow)]
    public void GetSnapshots_ShouldClassifyTheAverage_WhenItLiesAroundABoundary(double milliseconds, PerformanceBucket expected)
    {
        var store = Store();

        Record(store, News, milliseconds);

        store.GetSnapshots().ShouldHaveSingleItem().Performance.ShouldBe(expected);
    }

    [Fact]
    public void GetSnapshots_ShouldUseTheGivenThresholds_WhenTheyAreCustom()
    {
        var store = new StatisticsStore(_clock, new PerformanceThresholds(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20)));

        Record(store, News, 50);

        var snapshot = store.GetSnapshots().ShouldHaveSingleItem();
        snapshot.Performance.ShouldBe(PerformanceBucket.Slow);
        snapshot.Slow.Count.ShouldBe(1);
    }

    [Fact]
    public void GetSnapshots_ShouldCountFailuresAsTimedRequests_WhenTheUpstreamFailed()
    {
        var store = Store();

        store.RecordRequest(News, TimeSpan.FromMilliseconds(120), RequestOutcome.Failure, attempts: 3);
        store.RecordRequest(News, TimeSpan.FromMilliseconds(80), RequestOutcome.Success, attempts: 1);

        var snapshot = store.GetSnapshots().ShouldHaveSingleItem();
        snapshot.TotalRequests.ShouldBe(2);
        snapshot.Failures.ShouldBe(1);
        snapshot.Retries.ShouldBe(2);
        snapshot.AverageDuration.ShouldBe(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void GetSnapshots_ShouldCountCancelledRequestsApartFromTheTimings_WhenTheCallerGaveUp()
    {
        var store = Store();
        Record(store, News, 150);

        store.RecordRequest(News, TimeSpan.FromMilliseconds(5), RequestOutcome.Cancelled, attempts: 2);

        var snapshot = store.GetSnapshots().ShouldHaveSingleItem();
        snapshot.TotalRequests.ShouldBe(1);
        snapshot.Cancelled.ShouldBe(1);
        snapshot.Failures.ShouldBe(0);
        snapshot.Retries.ShouldBe(1);
        snapshot.AverageDuration.ShouldBe(TimeSpan.FromMilliseconds(150));
        snapshot.Recent.Count.ShouldBe(1);
    }

    [Fact]
    public void GetSnapshots_ShouldCountARejectionApartFromTheTimings_WhenNoAttemptReachedTheUpstream()
    {
        var store = Store();
        Record(store, News, 150);

        store.RecordRequest(News, TimeSpan.FromMilliseconds(1), RequestOutcome.Rejected, attempts: 0);

        var snapshot = store.GetSnapshots().ShouldHaveSingleItem();
        snapshot.Rejected.ShouldBe(1);
        snapshot.TotalRequests.ShouldBe(1);
        snapshot.Failures.ShouldBe(0);
        snapshot.Retries.ShouldBe(0);
        snapshot.AverageDuration.ShouldBe(TimeSpan.FromMilliseconds(150));
        snapshot.Fast.ShouldBe(BucketStatistics.Empty);
    }

    [Fact]
    public void Recent_ShouldKeepTheNewerSlice_WhenTheClockStepsBack()
    {
        var store = Store();
        Record(store, News, 100);
        _clock.Advance(TimeSpan.FromMinutes(5));
        Record(store, News, 300);

        _clock.Advance(TimeSpan.FromMinutes(-5));
        Record(store, News, 200);
        _clock.Advance(TimeSpan.FromMinutes(5));

        var snapshot = store.GetSnapshots().ShouldHaveSingleItem();
        snapshot.TotalRequests.ShouldBe(3);
        snapshot.Recent.Count.ShouldBe(1);
        snapshot.Recent.AverageDuration.ShouldBe(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenThresholdsAreMissing()
    {
        Should.Throw<ArgumentNullException>(() => new StatisticsStore(_clock, null!));
    }

    [Fact]
    public void GetSnapshots_ShouldReportCacheHitsWithoutAPerformanceClass_WhenNoRequestCompleted()
    {
        var store = Store();

        store.RecordCacheHit(HackerNews);
        store.RecordCacheHit(HackerNews);

        var snapshot = store.GetSnapshots().ShouldHaveSingleItem();
        snapshot.CacheHits.ShouldBe(2);
        snapshot.TotalRequests.ShouldBe(0);
        snapshot.AverageDuration.ShouldBe(TimeSpan.Zero);
        snapshot.Performance.ShouldBeNull();
        snapshot.Fast.ShouldBe(BucketStatistics.Empty);
    }

    [Fact]
    public void GetSnapshots_ShouldKeepOneEntryPerApiOrderedBySourceThenPrimaryFirst_WhenSeveralApisAreRecorded()
    {
        var store = Store();

        Record(store, Weather, 10);
        Record(store, HackerNews, 20);
        Record(store, News, 30, 30);

        store.GetSnapshots().Select(s => (s.Api, s.TotalRequests)).ShouldBe([(News, 2L), (HackerNews, 1L), (Weather, 1L)]);
    }

    [Fact]
    public void GetSnapshots_ShouldNotChangeAnEarlierSnapshot_WhenMoreIsRecorded()
    {
        var store = Store();
        Record(store, News, 50);
        var before = store.GetSnapshots();

        Record(store, News, 250);

        before.ShouldHaveSingleItem().TotalRequests.ShouldBe(1);
        store.GetSnapshots().ShouldHaveSingleItem().TotalRequests.ShouldBe(2);
    }

    [Fact]
    public void Recent_ShouldCoverTheLastFiveMinutes_WhenRequestsAreSpreadOverTime()
    {
        var store = Store();
        Record(store, News, 100);
        _clock.Advance(TimeSpan.FromMinutes(4));
        Record(store, News, 300, 300);

        var recent = store.GetSnapshots().ShouldHaveSingleItem().Recent;

        recent.Length.ShouldBe(TimeSpan.FromMinutes(5));
        recent.Count.ShouldBe(3);
        recent.AverageDuration.ShouldBe(TimeSpan.FromTicks(TimeSpan.FromMilliseconds(700).Ticks / 3));
    }

    [Fact]
    public void Recent_ShouldDropOldRequestsButKeepTheLifetimeTotals_WhenTheWindowMovesOn()
    {
        var store = Store();
        Record(store, News, 100);
        _clock.Advance(TimeSpan.FromMinutes(4));
        Record(store, News, 300);

        _clock.Advance(TimeSpan.FromSeconds(61));

        var snapshot = store.GetSnapshots().ShouldHaveSingleItem();
        snapshot.Recent.Count.ShouldBe(1);
        snapshot.Recent.AverageDuration.ShouldBe(TimeSpan.FromMilliseconds(300));
        snapshot.TotalRequests.ShouldBe(2);
    }

    [Fact]
    public void Recent_ShouldNotReuseAnOldSlice_WhenTheClockWrapsAroundTheRingBuffer()
    {
        var store = Store();
        Record(store, News, 100);
        _clock.Advance(TimeSpan.FromMinutes(5));

        Record(store, News, 300);

        var recent = store.GetSnapshots().ShouldHaveSingleItem().Recent;
        recent.Count.ShouldBe(1);
        recent.AverageDuration.ShouldBe(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void Recent_ShouldBeEmpty_WhenNothingHappenedForLongerThanTheWindow()
    {
        var store = Store();
        Record(store, News, 100);

        _clock.Advance(TimeSpan.FromHours(1));

        store.GetSnapshots().ShouldHaveSingleItem().Recent.ShouldBe(new WindowStatistics(TimeSpan.FromMinutes(5), 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task RecordRequest_ShouldKeepEveryCountAndTotalExact_WhenTenThousandRecordsRunInParallelWithReaders()
    {
        const int Records = 10_000;
        var store = Store();
        using var done = new CancellationTokenSource();
        var violations = 0;
        var reader = Task.Run(() =>
        {
            while (!done.IsCancellationRequested)
            {
                foreach (var snapshot in store.GetSnapshots())
                {
                    if (!IsConsistent(snapshot))
                    {
                        Interlocked.Increment(ref violations);
                    }
                }
            }
        }, TestContext.Current.CancellationToken);

        await Parallel.ForAsync(0, Records, TestContext.Current.CancellationToken, (i, _) =>
        {
            store.RecordRequest(i % 2 == 0 ? News : HackerNews, Duration(i), Outcome(i), attempts: 1 + (i % 3));
            if (i % 10 == 0)
            {
                store.RecordCacheHit(News);
            }

            return ValueTask.CompletedTask;
        });
        await done.CancelAsync();
        await reader;

        violations.ShouldBe(0);
        var snapshots = store.GetSnapshots();
        var completed = Enumerable.Range(0, Records).Where(i => Outcome(i) != RequestOutcome.Cancelled).ToList();
        snapshots.Sum(s => s.TotalRequests).ShouldBe(completed.Count);
        snapshots.Sum(s => s.Cancelled).ShouldBe(Records - completed.Count);
        snapshots.Sum(s => s.Failures).ShouldBe(completed.Count(i => Outcome(i) == RequestOutcome.Failure));
        snapshots.Sum(s => s.Retries).ShouldBe(Enumerable.Range(0, Records).Sum(i => (long)(i % 3)));
        snapshots.Single(s => s.Api == News).CacheHits.ShouldBe(Records / 10);
        snapshots.Sum(s => s.Recent.Count).ShouldBe(completed.Count);
        snapshots.Sum(s => s.Slow.Count).ShouldBe(completed.Count(i => Duration(i) > PerformanceThresholds.Default.SlowAbove));
        snapshots.ShouldAllBe(s => IsConsistent(s));
    }

    private static TimeSpan Duration(int i) => TimeSpan.FromMilliseconds(i % 300);

    private static RequestOutcome Outcome(int i) => (i % 7) switch
    {
        0 => RequestOutcome.Failure,
        1 => RequestOutcome.Cancelled,
        _ => RequestOutcome.Success,
    };

    /// <summary>The buckets add up to the totals, and the overall average lies between the smallest and largest bucket average.</summary>
    private static bool IsConsistent(ApiStatisticsResult snapshot)
    {
        BucketStatistics[] buckets = [snapshot.Fast, snapshot.Average, snapshot.Slow];
        var used = buckets.Where(b => b.Count > 0).ToList();
        return buckets.Sum(b => b.Count) == snapshot.TotalRequests
            && (used.Count == 0
                ? snapshot.AverageDuration == TimeSpan.Zero
                : snapshot.AverageDuration >= used.Min(b => b.AverageDuration) && snapshot.AverageDuration <= used.Max(b => b.AverageDuration))
            && snapshot.Failures <= snapshot.TotalRequests
            && snapshot.Recent.Count <= snapshot.TotalRequests;
    }

    private StatisticsStore Store() => new(_clock, PerformanceThresholds.Default);

    private static void Record(StatisticsStore store, UpstreamApi api, params double[] milliseconds)
    {
        foreach (var duration in milliseconds)
        {
            store.RecordRequest(api, TimeSpan.FromMilliseconds(duration), RequestOutcome.Success, attempts: 1);
        }
    }
}
