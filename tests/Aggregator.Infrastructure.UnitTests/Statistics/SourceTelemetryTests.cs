using System.Diagnostics.Metrics;
using Aggregator.Application.Sources;
using Aggregator.Application.Statistics;
using Aggregator.Application.Telemetry;
using Aggregator.Domain.Statistics;
using Aggregator.Infrastructure.Statistics;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace Aggregator.Infrastructure.UnitTests.Statistics;

public sealed class SourceTelemetryTests : IDisposable
{
    private static readonly UpstreamApi News = new("news", null);
    private static readonly UpstreamApi HackerNews = new("news", "hacker-news");

    private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly StatisticsStore _store = new(new ManualTimeProvider(DateTimeOffset.UnixEpoch), PerformanceThresholds.Default);
    private readonly SourceTelemetry _telemetry;
    private readonly MetricCollector<double> _durations;
    private readonly MetricCollector<long> _requests;

    public SourceTelemetryTests()
    {
        var meters = _services.GetRequiredService<IMeterFactory>();
        _telemetry = new SourceTelemetry(_store, meters, PerformanceThresholds.Default);
        _durations = new MetricCollector<double>(meters, AggregatorDiagnostics.Name, SourceTelemetry.DurationName);
        _requests = new MetricCollector<long>(meters, AggregatorDiagnostics.Name, SourceTelemetry.RequestsName);
    }

    [Fact]
    public void RecordRequest_ShouldWriteTheStoreAndTheMeter_WhenARequestCompletes()
    {
        _telemetry.RecordRequest(News, TimeSpan.FromMilliseconds(150), RequestOutcome.Success, attempts: 2);

        _store.GetSnapshots().ShouldHaveSingleItem().ShouldSatisfyAllConditions(
            s => s.TotalRequests.ShouldBe(1),
            s => s.Retries.ShouldBe(1));
        var duration = _durations.GetMeasurementSnapshot().ShouldHaveSingleItem();
        duration.Value.ShouldBe(0.15, tolerance: 1e-9);
        duration.Tags.ShouldBe(Tags("news", "primary", "success"), ignoreOrder: true);
        var request = _requests.GetMeasurementSnapshot().ShouldHaveSingleItem();
        request.Value.ShouldBe(1);
        request.Tags.ShouldBe(Tags("news", "primary", "success"), ignoreOrder: true);
    }

    [Fact]
    public void RecordRequest_ShouldTagTheSecondaryProviderAndOutcome_WhenTheSecondaryFailed()
    {
        _telemetry.RecordRequest(HackerNews, TimeSpan.FromMilliseconds(40), RequestOutcome.Failure, attempts: 3);

        _durations.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags.ShouldBe(Tags("news", "hacker-news", "failure"), ignoreOrder: true);
        _store.GetSnapshots().ShouldHaveSingleItem().Api.ShouldBe(HackerNews);
    }

    [Fact]
    public void RecordRequest_ShouldCountButNotTime_WhenTheCallerCancelled()
    {
        _telemetry.RecordRequest(News, TimeSpan.FromMilliseconds(5), RequestOutcome.Cancelled, attempts: 1);

        _durations.GetMeasurementSnapshot().ShouldBeEmpty();
        _requests.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["outcome"].ShouldBe("cancelled");
        _store.GetSnapshots().ShouldHaveSingleItem().Cancelled.ShouldBe(1);
    }

    [Fact]
    public void RecordRequest_ShouldCountButNotTime_WhenNoAttemptWasSent()
    {
        _telemetry.RecordRequest(News, TimeSpan.FromMilliseconds(1), RequestOutcome.Rejected, attempts: 0);

        _durations.GetMeasurementSnapshot().ShouldBeEmpty();
        _requests.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["outcome"].ShouldBe("rejected");
        _store.GetSnapshots().ShouldHaveSingleItem().Rejected.ShouldBe(1);
    }

    [Fact]
    public void RecordRequest_ShouldGiveTheMeterTheSameNumbersAsTheStore_WhenManyRequestsAreRecorded()
    {
        double[] milliseconds = [12, 99, 100, 180, 200, 201, 950];
        foreach (var value in milliseconds)
        {
            _telemetry.RecordRequest(News, TimeSpan.FromMilliseconds(value), RequestOutcome.Success, attempts: 1);
        }

        var snapshot = _store.GetSnapshots().ShouldHaveSingleItem();
        var measured = _durations.GetMeasurementSnapshot();
        measured.Count.ShouldBe((int)snapshot.TotalRequests);
        (measured.Sum(m => m.Value) / measured.Count).ShouldBe(snapshot.AverageDuration.TotalSeconds, tolerance: 1e-6);
        measured.Count(m => m.Value > PerformanceThresholds.Default.SlowAbove.TotalSeconds).ShouldBe((int)snapshot.Slow.Count);
    }

    [Fact]
    public void RecordCacheHit_ShouldOnlyWriteTheStore_WhenAnAnswerCameFromTheCache()
    {
        _telemetry.RecordCacheHit(HackerNews);

        _store.GetSnapshots().ShouldHaveSingleItem().CacheHits.ShouldBe(1);
        _durations.GetMeasurementSnapshot().ShouldBeEmpty();
        _requests.GetMeasurementSnapshot().ShouldBeEmpty();
    }

    [Fact]
    public void DurationHistogram_ShouldAdviseBucketBoundariesAtTheThresholds_WhenCreated()
    {
        var histogram = _durations.Instrument.ShouldBeAssignableTo<Histogram<double>>()!;

        histogram.Unit.ShouldBe("s");
        var boundaries = histogram.Advice!.HistogramBucketBoundaries!;
        boundaries.ShouldContain(0.1);
        boundaries.ShouldContain(0.2);
        boundaries.ShouldBe([.. boundaries.Order()]);
    }

    public void Dispose()
    {
        _durations.Dispose();
        _requests.Dispose();
        _services.Dispose();
    }

    private static Dictionary<string, object?> Tags(string source, string provider, string outcome) =>
        new() { ["source"] = source, ["provider"] = provider, ["outcome"] = outcome };
}
