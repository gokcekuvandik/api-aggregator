using System.Diagnostics;
using System.Diagnostics.Metrics;
using Aggregator.Application.Sources;
using Aggregator.Application.Statistics;
using Aggregator.Application.Telemetry;
using Aggregator.Domain.Statistics;

namespace Aggregator.Infrastructure.Statistics;

/// <summary>
/// Writes each measurement to the statistics store and to the <c>Aggregator</c> meter, so the endpoint and the dashboards read the same numbers.
/// Only requests the upstream answered or failed are timed; cancelled and rejected ones are only counted. Tags are the source, the provider (<c>primary</c> or the secondary's name) and the outcome.
/// </summary>
internal sealed class SourceTelemetry : ISourceTelemetry
{
    public const string DurationName = "aggregator.source.request.duration";
    public const string RequestsName = "aggregator.source.requests";

    private const string PrimaryProvider = "primary";

    private static readonly double[] ExtraBoundarySeconds = [0.025, 0.05, 0.5, 1, 2.5, 5, 10];

    private readonly StatisticsStore _store;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _requests;

    public SourceTelemetry(StatisticsStore store, IMeterFactory meterFactory, PerformanceThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(thresholds);

        _store = store;
        var meter = meterFactory.Create(AggregatorDiagnostics.Name);
        _duration = meter.CreateHistogram(
            DurationName,
            unit: "s",
            description: "Upstream request duration as the caller saw it, retries included.",
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = Boundaries(thresholds) });
        _requests = meter.CreateCounter<long>(RequestsName, unit: "{request}", description: "Upstream requests by outcome.");
    }

    public void RecordRequest(UpstreamApi api, TimeSpan duration, RequestOutcome outcome, int attempts)
    {
        _store.RecordRequest(api, duration, outcome, attempts);

        var tags = ApiTags(api);
        tags.Add("outcome", OutcomeTag(outcome));
        _requests.Add(1, tags);
        if (outcome is RequestOutcome.Success or RequestOutcome.Failure)
        {
            _duration.Record(duration.TotalSeconds, tags);
        }
    }

    public void RecordCacheHit(UpstreamApi api) => _store.RecordCacheHit(api);

    /// <summary>The provider's name, or <c>primary</c> for a source's primary provider, which has none.</summary>
    internal static string ProviderTag(UpstreamApi api) => api.Provider ?? PrimaryProvider;

    /// <summary>The <c>source</c> and <c>provider</c> tags every Aggregator instrument about an external API carries.</summary>
    internal static TagList ApiTags(UpstreamApi api) => new() { { "source", api.Source }, { "provider", ProviderTag(api) } };

    private static double[] Boundaries(PerformanceThresholds thresholds) =>
        [.. ExtraBoundarySeconds.Append(thresholds.FastBelow.TotalSeconds).Append(thresholds.SlowAbove.TotalSeconds).Distinct().Order()];

    private static string OutcomeTag(RequestOutcome outcome) => outcome switch
    {
        RequestOutcome.Success => "success",
        RequestOutcome.Failure => "failure",
        RequestOutcome.Cancelled => "cancelled",
        RequestOutcome.Rejected => "rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown request outcome."),
    };
}
