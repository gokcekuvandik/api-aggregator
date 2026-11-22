using Aggregator.Domain.Items;

namespace Aggregator.Application.Sources;

/// <summary>The source answered live.</summary>
public sealed record SourceSuccess(IReadOnlyList<AggregatedItem> Items) : SourceResult(Items);
