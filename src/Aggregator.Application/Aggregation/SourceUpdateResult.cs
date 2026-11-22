using Aggregator.Domain.Items;

namespace Aggregator.Application.Aggregation;

/// <summary>One source's report and its filtered, sorted items, streamed as soon as that source completes.</summary>
public sealed record SourceUpdateResult(SourceReport Report, IReadOnlyList<AggregatedItem> Items);
