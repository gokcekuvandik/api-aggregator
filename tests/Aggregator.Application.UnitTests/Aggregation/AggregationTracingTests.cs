using System.Collections.Concurrent;
using System.Diagnostics;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Aggregator.Application.Telemetry;
using Aggregator.Application.UnitTests.TestData;

namespace Aggregator.Application.UnitTests.Aggregation;

/// <summary>
/// Tracing: an <c>aggregate</c> span with one <c>aggregate.source</c> child per source.
/// The listener is process-wide, so each test runs under its own root span and reads back only that trace.
/// </summary>
public sealed class AggregationTracingTests : IDisposable
{
    private static readonly AggregationQuery Query = new(Page: 1, PageSize: 20) { Text = "secret-search", City = "Atlantis" };

    private readonly ConcurrentQueue<Activity> _stopped = new();
    private readonly ActivityListener _listener;
    private readonly Activity _root;

    public AggregationTracingTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AggregatorDiagnostics.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
        _root = new Activity("test-root").Start();
    }

    [Fact]
    public async Task AggregateAsync_ShouldTraceOneChildSpanPerSource_WhenSourcesAreFetchedInParallel()
    {
        var service = Service(
            TestSources.Returning("news", new SourceSuccess([])),
            TestSources.Returning("weather", new SourceCached([], TimeSpan.FromMinutes(1))),
            TestSources.Returning("repositories", new SourceFallback([], TimeSpan.Zero)));

        await service.AggregateAsync(Query, TestContext.Current.CancellationToken);

        var aggregate = Single("aggregate");
        aggregate.ParentSpanId.ShouldBe(_root.SpanId);
        aggregate.GetTagItem("aggregator.mode").ShouldBe("page");
        aggregate.GetTagItem("aggregator.source_count").ShouldBe(3);
        aggregate.GetTagItem("aggregator.outcome").ShouldBe("degraded");
        var children = Spans("aggregate.source");
        children.ShouldAllBe(child => child.ParentSpanId == aggregate.SpanId);
        children.ToDictionary(child => (string)child.GetTagItem("aggregator.source")!, child => child.GetTagItem("aggregator.result"))
            .ShouldBe(new Dictionary<string, object?> { ["news"] = "success", ["weather"] = "cached", ["repositories"] = "fallback" }, ignoreOrder: true);
    }

    [Fact]
    public async Task AggregateAsync_ShouldMarkTheFailedSourceSpanAsAnError_WhenOneSourceFails()
    {
        var service = Service(TestSources.Returning("news", new SourceSuccess([])), TestSources.Returning("weather", new SourceFailure("down")));

        await service.AggregateAsync(Query, TestContext.Current.CancellationToken);

        var children = Spans("aggregate.source").ToDictionary(child => (string)child.GetTagItem("aggregator.source")!);
        children["weather"].Status.ShouldBe(ActivityStatusCode.Error);
        children["weather"].GetTagItem("aggregator.result").ShouldBe("failure");
        children["news"].Status.ShouldBe(ActivityStatusCode.Unset);
        Single("aggregate").Status.ShouldBe(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task AggregateAsync_ShouldNameTheExceptionOnTheSourceSpan_WhenASourceThrows()
    {
        var broken = TestSources.Throwing("broken", new InvalidOperationException("bug"));

        await Should.ThrowAsync<InvalidOperationException>(() => Service(broken).AggregateAsync(Query, TestContext.Current.CancellationToken));

        var source = Single("aggregate.source");
        source.Status.ShouldBe(ActivityStatusCode.Error);
        source.StatusDescription.ShouldBe(nameof(InvalidOperationException));
        Single("aggregate").GetTagItem("aggregator.outcome").ShouldBe("error");
    }

    [Fact]
    public async Task AggregateAsync_ShouldTagTheSourceSpanAsCancelled_WhenTheCallerGivesUp()
    {
        using var cts = new CancellationTokenSource();
        var pending = Service(TestSources.Hanging("slow")).AggregateAsync(Query, cts.Token);

        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => pending);
        Single("aggregate.source").GetTagItem("aggregator.result").ShouldBe("cancelled");
        Single("aggregate").GetTagItem("aggregator.outcome").ShouldBe("cancelled");
    }

    [Fact]
    public async Task AggregateAsync_ShouldMarkTheAggregateSpanAsAnError_WhenEverySourceFails()
    {
        var service = Service(TestSources.Returning("news", new SourceFailure("down")));

        await service.AggregateAsync(Query, TestContext.Current.CancellationToken);

        var aggregate = Single("aggregate");
        aggregate.Status.ShouldBe(ActivityStatusCode.Error);
        aggregate.GetTagItem("aggregator.outcome").ShouldBe("failed");
    }

    [Fact]
    public async Task StreamAsync_ShouldParentEverySourceSpanToTheAggregateSpan_WhenSourcesAreYieldedOneByOne()
    {
        var service = Service(TestSources.Returning("news", new SourceSuccess([])), TestSources.Returning("weather", new SourceSuccess([])));

        await foreach (var _ in service.StreamAsync(Query, TestContext.Current.CancellationToken))
        {
            // Each yield hands control back to the consumer, whose current activity is the root.
        }

        var aggregate = Single("aggregate");
        aggregate.GetTagItem("aggregator.mode").ShouldBe("stream");
        aggregate.GetTagItem("aggregator.outcome").ShouldBe("complete");
        Spans("aggregate.source").Count.ShouldBe(2);
        Spans("aggregate.source").ShouldAllBe(child => child.ParentSpanId == aggregate.SpanId);
    }

    [Fact]
    public async Task Spans_ShouldNeverCarryTheCallersSearch_WhenTheQueryHasTextAndCity()
    {
        var service = Service(TestSources.Returning("news", new SourceSuccess([])), TestSources.Returning("weather", new SourceFailure("down")));

        await service.AggregateAsync(Query, TestContext.Current.CancellationToken);

        var values = Trace().SelectMany(span => span.TagObjects.Select(tag => tag.Value?.ToString()))
            .Concat(Trace().SelectMany(span => span.Events.SelectMany(e => e.Tags.Select(tag => tag.Value?.ToString()))))
            .ToList();
        values.ShouldNotBeEmpty();
        values.ShouldNotContain(value => value!.Contains("secret-search", StringComparison.Ordinal) || value.Contains("Atlantis", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        _root.Dispose();
        _listener.Dispose();
    }

    private static AggregationService Service(params IDataSource[] sources) => new(new DataSourceRegistry(sources), TestTelemetry.Unobserved);

    private List<Activity> Trace() => [.. _stopped.Where(span => span.TraceId == _root.TraceId)];

    private List<Activity> Spans(string name) => [.. Trace().Where(span => span.OperationName == name)];

    private Activity Single(string name) => Spans(name).ShouldHaveSingleItem();
}
