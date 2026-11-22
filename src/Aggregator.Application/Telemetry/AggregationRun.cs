using System.Diagnostics;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;

namespace Aggregator.Application.Telemetry;

/// <summary>
/// One aggregation as telemetry: an <c>aggregate</c> span with one <c>aggregate.source</c> child per source, and its duration recorded once on dispose.
/// An aggregation that never completes is an error when a source threw, otherwise cancelled.
/// </summary>
internal sealed class AggregationRun : IDisposable
{
    internal const string AggregateSpanName = "aggregate";
    internal const string SourceSpanName = "aggregate.source";

    private const string Complete = "complete";
    private const string Degraded = "degraded";
    private const string Failed = "failed";
    private const string Errored = "error";
    private const string Cancelled = "cancelled";

    private readonly AggregationTelemetry _telemetry;
    private readonly string _mode;
    private readonly Activity? _span;
    private readonly long _started = Stopwatch.GetTimestamp();
    private string? _outcome;
    private volatile bool _errored;

    public AggregationRun(AggregationTelemetry telemetry, string mode, int sourceCount)
    {
        _telemetry = telemetry;
        _mode = mode;
        _span = AggregatorDiagnostics.ActivitySource.StartActivity(AggregateSpanName);
        _span?.SetTag("aggregator.mode", mode).SetTag("aggregator.source_count", sourceCount);
    }

    /// <summary>Starts a source's span as a child of the aggregate span.</summary>
    public SourceSpan StartSource(string name) =>
        new(this, AggregatorDiagnostics.ActivitySource.StartActivity(SourceSpanName, ActivityKind.Internal, _span?.Context ?? default)
            ?.SetTag(AggregatorDiagnostics.SourceTag, name));

    public void Completed(IReadOnlyCollection<SourceReport> reports) => _outcome = OutcomeOf(reports);

    public void Dispose()
    {
        var outcome = _outcome ?? (_errored ? Errored : Cancelled);
        _telemetry.RecordAggregation(Stopwatch.GetElapsedTime(_started), _mode, outcome);
        if (_span is null)
        {
            return;
        }

        _span.SetTag("aggregator.outcome", outcome);
        if (outcome is Failed or Errored)
        {
            _span.SetStatus(ActivityStatusCode.Error);
        }

        _span.Dispose();
    }

    private static string OutcomeOf(IReadOnlyCollection<SourceReport> reports) =>
        reports.All(report => report.Result is SourceSuccess or SourceCached) ? Complete
        : reports.All(report => report.Result is SourceFailure) ? Failed
        : Degraded;

    /// <summary>One source's span within a run.</summary>
    internal readonly struct SourceSpan(AggregationRun run, Activity? span) : IDisposable
    {
        private const string ResultTag = "aggregator.result";

        public void Answered(SourceResult result)
        {
            span?.SetTag(ResultTag, ResultOf(result));
            if (result is SourceFailure)
            {
                span?.SetStatus(ActivityStatusCode.Error);
            }
        }

        public void Cancelled() => span?.SetTag(ResultTag, AggregationRun.Cancelled);

        public void Threw(Exception exception)
        {
            run._errored = true;
            span?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
        }

        public void Dispose() => span?.Dispose();

        private static string ResultOf(SourceResult result) => result switch
        {
            SourceSuccess => "success",
            SourceCached => "cached",
            SourceFallback => "fallback",
            SourceFailure => "failure",
            _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown source result."),
        };
    }
}
