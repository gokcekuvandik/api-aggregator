using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Aggregator.Application.Statistics;
using Aggregator.Application.Telemetry;
using Aggregator.Infrastructure.Statistics;

namespace Aggregator.Infrastructure.Sources.Resilience;

/// <summary>An external API whose circuit breaker state is reported, registered once per resilient client.</summary>
internal sealed record TrackedCircuit(UpstreamApi Api);

/// <summary>
/// Holds each external API's circuit breaker state for the <c>aggregator.source.circuit_state</c> gauge: 0 closed, 1 half-open, 2 open.
/// Every registered API reads closed from startup; opening also adds an event to the current span.
/// </summary>
internal sealed class CircuitStateTracker
{
    public const string CircuitStateName = "aggregator.source.circuit_state";

    internal const string CircuitOpenedEventName = "aggregator.circuit.opened";

    private const int ClosedState = 0;
    private const int HalfOpenState = 1;
    private const int OpenState = 2;

    private readonly ConcurrentDictionary<UpstreamApi, int> _states = new();

    public CircuitStateTracker(IMeterFactory meterFactory, IEnumerable<TrackedCircuit> circuits)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(circuits);
        foreach (var circuit in circuits)
        {
            _states.TryAdd(circuit.Api, ClosedState);
        }

        meterFactory.Create(AggregatorDiagnostics.Name).CreateObservableGauge(
            CircuitStateName, Observe, unit: "{state}", description: "Circuit breaker state per external API: 0 closed, 1 half-open, 2 open.");
    }

    public void Opened(UpstreamApi api)
    {
        _states[api] = OpenState;
        if (Activity.Current is { IsAllDataRequested: true } span)
        {
            span.AddEvent(new ActivityEvent(
                CircuitOpenedEventName,
                tags: new ActivityTagsCollection { [AggregatorDiagnostics.SourceTag] = api.Source, [AggregatorDiagnostics.ProviderTag] = SourceTelemetry.ProviderTag(api) }));
        }
    }

    public void HalfOpened(UpstreamApi api) => _states[api] = HalfOpenState;

    public void Closed(UpstreamApi api) => _states[api] = ClosedState;

    private IEnumerable<Measurement<int>> Observe() =>
        _states.Select(state => new Measurement<int>(state.Value, SourceTelemetry.ApiTags(state.Key)));
}
