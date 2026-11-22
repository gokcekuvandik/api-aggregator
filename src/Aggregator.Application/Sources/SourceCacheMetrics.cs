using System.Diagnostics.Metrics;
using Aggregator.Application.Telemetry;

namespace Aggregator.Application.Sources;

/// <summary>Counts source cache lookups by source and result: <c>hit</c>, <c>miss</c>, or <c>stale</c> when last-known-good items were served.</summary>
public sealed class SourceCacheMetrics
{
    public const string LookupsName = "aggregator.cache.lookups";

    private readonly Counter<long> _lookups;

    public SourceCacheMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _lookups = meterFactory.Create(AggregatorDiagnostics.Name).CreateCounter<long>(LookupsName, unit: "{lookup}", description: "Source cache lookups by result.");
    }

    internal void Hit(string source) => Record(source, "hit");

    internal void Miss(string source) => Record(source, "miss");

    internal void Stale(string source) => Record(source, "stale");

    private void Record(string source, string result) =>
        _lookups.Add(1, new KeyValuePair<string, object?>("source", source), new KeyValuePair<string, object?>("result", result));
}
