using Aggregator.Domain.Items;

namespace Aggregator.Application.Aggregation.Sorting;

/// <summary>
/// Sorts by the requested key, then by fixed tie-breakers, so the same request always pages the same way.
/// Ties on relevance go to the newer item; ties on date or source go to the more relevant one; the id decides last.
/// </summary>
public static class ItemOrdering
{
    public static IReadOnlyList<AggregatedItem> Sort(IEnumerable<AggregatedItem> items, ItemSortField field, SortDirection direction)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown sort direction.");
        }

        var ordered = field switch
        {
            ItemSortField.Relevance => By(items, item => item.Relevance, direction).ThenByDescending(item => item.PublishedAt),
            ItemSortField.Date => By(items, item => item.PublishedAt, direction).ThenByDescending(item => item.Relevance),
            ItemSortField.Source => By(items, item => item.Source, direction, StringComparer.OrdinalIgnoreCase).ThenByDescending(item => item.Relevance),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown sort field."),
        };

        return [.. ordered.ThenBy(item => item.Id, StringComparer.Ordinal)];
    }

    private static IOrderedEnumerable<AggregatedItem> By<TKey>(
        IEnumerable<AggregatedItem> items,
        Func<AggregatedItem, TKey> key,
        SortDirection direction,
        IComparer<TKey>? comparer = null) =>
        direction == SortDirection.Ascending ? items.OrderBy(key, comparer) : items.OrderByDescending(key, comparer);
}
