using Aggregator.Application.Sources;

namespace Aggregator.Application.Aggregation;

/// <summary>What one source did during an aggregation, and how long it took.</summary>
public sealed record SourceReport(string Name, SourceResult Result, TimeSpan Duration)
{
    /// <summary>True when the source returned live, cached or fallback data.</summary>
    public bool HasData => Result is not SourceFailure;
}
