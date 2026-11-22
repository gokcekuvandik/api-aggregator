using Aggregator.Application.Sources;
using Aggregator.Domain.Items;

namespace Aggregator.Infrastructure.Sources.Caching;

/// <summary>
/// The serialized form of cached source items, so domain types stay free of serializer attributes.
/// The same shape is written to the in-memory cache and to a distributed one, if one is added.
/// </summary>
internal sealed record SourceItemsPayload(DateTimeOffset StoredAt, string? Provider, IReadOnlyList<ItemPayload> Items)
{
    public static SourceItemsPayload From(CachedItems entry) =>
        new(entry.StoredAt, entry.Provider, [.. entry.Items.Select(ItemPayload.From)]);

    public IReadOnlyList<AggregatedItem> ToItems() => [.. Items.Select(item => item.ToItem())];

    public CachedItems ToCachedItems() => new(ToItems(), StoredAt, Provider);
}
