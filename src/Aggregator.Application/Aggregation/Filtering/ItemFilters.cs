using Aggregator.Domain.Items;

namespace Aggregator.Application.Aggregation.Filtering;

/// <summary>Builds the filters a query asks for and applies them together.</summary>
public static class ItemFilters
{
    public static IReadOnlyList<IItemFilter> For(AggregationQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        List<IItemFilter> filters = [];
        if (query.Category is not null)
        {
            filters.Add(new CategoryFilter(query.Category));
        }

        if (query.From is not null || query.To is not null)
        {
            filters.Add(new DateRangeFilter(query.From, query.To));
        }

        return filters;
    }

    public static IEnumerable<AggregatedItem> Apply(IEnumerable<AggregatedItem> items, IReadOnlyList<IItemFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(filters);
        return items.Where(item => filters.All(filter => filter.Matches(item)));
    }
}
