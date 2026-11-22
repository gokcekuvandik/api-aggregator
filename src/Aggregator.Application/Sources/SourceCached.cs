using Aggregator.Domain.Items;

namespace Aggregator.Application.Sources;

/// <summary>The answer came from a fresh cache entry stored <paramref name="Age"/> ago, live from the primary or from the named secondary provider.</summary>
public sealed record SourceCached(IReadOnlyList<AggregatedItem> Items, TimeSpan Age, string? Provider = null) : SourceResult(Items);
