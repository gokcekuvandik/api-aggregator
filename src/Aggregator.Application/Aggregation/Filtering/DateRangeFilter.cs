using Aggregator.Domain.Items;

namespace Aggregator.Application.Aggregation.Filtering;

/// <summary>Keeps items published within the range; both bounds are inclusive and either may be open.</summary>
public sealed class DateRangeFilter : IItemFilter
{
    private readonly DateTimeOffset? _from;
    private readonly DateTimeOffset? _to;

    public DateRangeFilter(DateTimeOffset? from, DateTimeOffset? to)
    {
        if (from > to)
        {
            throw new ArgumentException("The start of the range must not be after its end.", nameof(from));
        }

        _from = from;
        _to = to;
    }

    public bool Matches(AggregatedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return (_from is not { } from || item.PublishedAt >= from)
            && (_to is not { } to || item.PublishedAt <= to);
    }
}
