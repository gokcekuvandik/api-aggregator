using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Aggregator.Application.Telemetry;

/// <summary>
/// The aggregation's own instruments on the <c>Aggregator</c> meter: fallback activations by source and result, and aggregation duration by mode and outcome.
/// Tags hold only fixed values and source names, never anything the caller typed.
/// </summary>
public sealed class AggregationTelemetry
{
    public const string FallbackActivationsName = "aggregator.fallback.activations";
    public const string AggregationDurationName = "aggregator.aggregation.duration";

    internal const string FallbackEventName = "aggregator.fallback";

    private static readonly double[] DurationBoundarySeconds = [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];

    private readonly Counter<long> _fallbacks;
    private readonly Histogram<double> _durations;

    public AggregationTelemetry(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(AggregatorDiagnostics.Name);
        _fallbacks = meter.CreateCounter<long>(
            FallbackActivationsName, unit: "{activation}", description: "Times a source's primary provider failed, by what stood in for it.");
        _durations = meter.CreateHistogram(
            AggregationDurationName,
            unit: "s",
            description: "Time to fetch every selected source, as the caller saw it; a stream also includes handing each answer on.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBoundarySeconds });
    }

    internal void SecondaryStoodIn(string source, string provider) => RecordFallback(source, "secondary", provider);

    internal void LastKnownGoodStoodIn(string source) => RecordFallback(source, "last_known_good", provider: null);

    internal void NothingStoodIn(string source) => RecordFallback(source, "none", provider: null);

    internal AggregationRun StartRun(string mode, int sourceCount) => new(this, mode, sourceCount);

    internal void RecordAggregation(TimeSpan duration, string mode, string outcome) =>
        _durations.Record(duration.TotalSeconds, new KeyValuePair<string, object?>("mode", mode), new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>Counts one fallback and, when the current span is recorded, adds an event to it.</summary>
    private void RecordFallback(string source, string result, string? provider)
    {
        _fallbacks.Add(1, new KeyValuePair<string, object?>("source", source), new KeyValuePair<string, object?>("result", result));
        if (Activity.Current is not { IsAllDataRequested: true } span)
        {
            return;
        }

        var tags = new ActivityTagsCollection { [AggregatorDiagnostics.SourceTag] = source, ["aggregator.fallback.result"] = result };
        if (provider is not null)
        {
            tags[AggregatorDiagnostics.ProviderTag] = provider;
        }

        span.AddEvent(new ActivityEvent(FallbackEventName, tags: tags));
    }
}
