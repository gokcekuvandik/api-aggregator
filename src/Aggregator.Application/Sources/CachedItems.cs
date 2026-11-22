using Aggregator.Domain.Items;

namespace Aggregator.Application.Sources;

/// <summary>Items a source answered live, when they were stored, and the secondary provider that answered, if any.</summary>
public sealed record CachedItems(IReadOnlyList<AggregatedItem> Items, DateTimeOffset StoredAt, string? Provider = null);
