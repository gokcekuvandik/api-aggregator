using Aggregator.Domain.Items;

namespace Aggregator.Application.Sources;

/// <summary>The last items a source answered for one search, and when they were stored.</summary>
public sealed record LastKnownGood(IReadOnlyList<AggregatedItem> Items, DateTimeOffset StoredAt);
