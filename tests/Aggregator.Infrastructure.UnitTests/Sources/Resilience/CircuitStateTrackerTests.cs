using System.Diagnostics;
using System.Diagnostics.Metrics;
using Aggregator.Application.Statistics;
using Aggregator.Application.Telemetry;
using Aggregator.Infrastructure.Sources.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace Aggregator.Infrastructure.UnitTests.Sources.Resilience;

public sealed class CircuitStateTrackerTests : IDisposable
{
    private static readonly UpstreamApi News = new("news", null);
    private static readonly UpstreamApi HackerNews = new("news", "hacker-news");

    private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly CircuitStateTracker _tracker;
    private readonly MetricCollector<int> _states;

    public CircuitStateTrackerTests()
    {
        var meters = _services.GetRequiredService<IMeterFactory>();
        _tracker = new CircuitStateTracker(meters, [new TrackedCircuit(News), new TrackedCircuit(HackerNews)]);
        _states = new MetricCollector<int>(meters, AggregatorDiagnostics.Name, CircuitStateTracker.CircuitStateName);
    }

    [Fact]
    public void Gauge_ShouldReportEveryRegisteredApiAsClosed_WhenNoStateHasChanged()
    {
        var states = Observe();

        states.Count.ShouldBe(2);
        states.ShouldAllBe(state => state.Value == 0);
        states.ShouldContain(state => (string?)state.Tags["source"] == "news" && (string?)state.Tags["provider"] == "primary");
    }

    [Fact]
    public void Gauge_ShouldFollowEachTransition_WhenTheCircuitOpensHalfOpensAndCloses()
    {
        _tracker.Opened(News);
        PrimaryState().ShouldBe(2);
        _tracker.HalfOpened(News);
        PrimaryState().ShouldBe(1);
        _tracker.Closed(News);
        PrimaryState().ShouldBe(0);
    }

    [Fact]
    public void Gauge_ShouldKeepProvidersApart_WhenOnlyThePrimaryOpens()
    {
        _tracker.Opened(News);

        var states = Observe().ToDictionary(measurement => (string)measurement.Tags["provider"]!, measurement => measurement.Value);
        states.ShouldBe(new Dictionary<string, int> { ["primary"] = 2, ["hacker-news"] = 0 }, ignoreOrder: true);
    }

    [Fact]
    public void Opened_ShouldAddACircuitOpenedEventToTheCurrentSpan_WhenASpanIsActive()
    {
        using var span = new Activity("test").Start();

        _tracker.Opened(HackerNews);

        var opened = span.Events.ShouldHaveSingleItem();
        opened.Name.ShouldBe("aggregator.circuit.opened");
        opened.Tags.ToDictionary().ShouldBe(
            new Dictionary<string, object?> { ["aggregator.source"] = "news", ["aggregator.provider"] = "hacker-news" }, ignoreOrder: true);
    }

    public void Dispose()
    {
        _states.Dispose();
        _services.Dispose();
    }

    private IReadOnlyList<CollectedMeasurement<int>> Observe()
    {
        _states.Clear();
        _states.RecordObservableInstruments();
        return _states.GetMeasurementSnapshot();
    }

    private int PrimaryState() => Observe().Single(state => (string?)state.Tags["provider"] == "primary").Value;
}
