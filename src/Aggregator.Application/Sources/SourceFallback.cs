using Aggregator.Domain.Items;

namespace Aggregator.Application.Sources;

/// <summary>
/// The primary provider failed. The items come from the named secondary provider (age zero),
/// or, when <paramref name="Provider"/> is null, from the last-known-good store at the given age.
/// </summary>
public sealed record SourceFallback(IReadOnlyList<AggregatedItem> Items, TimeSpan Age, string? Provider = null) : SourceResult(Items);
