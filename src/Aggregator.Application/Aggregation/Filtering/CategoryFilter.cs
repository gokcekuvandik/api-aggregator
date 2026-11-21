using Aggregator.Domain.Items;

namespace Aggregator.Application.Aggregation.Filtering;

/// <summary>Keeps items of one category, ignoring case.</summary>
public sealed class CategoryFilter : IItemFilter
{
    private readonly string _category;

    public CategoryFilter(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        _category = category;
    }

    public bool Matches(AggregatedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return string.Equals(item.Category, _category, StringComparison.OrdinalIgnoreCase);
    }
}
