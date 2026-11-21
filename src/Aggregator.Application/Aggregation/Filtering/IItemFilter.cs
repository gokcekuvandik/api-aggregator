using Aggregator.Domain.Items;

namespace Aggregator.Application.Aggregation.Filtering;

/// <summary>One condition an item must meet to stay in the result.</summary>
public interface IItemFilter
{
    bool Matches(AggregatedItem item);
}
