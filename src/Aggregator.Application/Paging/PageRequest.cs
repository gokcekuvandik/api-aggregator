namespace Aggregator.Application.Paging;

/// <summary>Offset paging over an in-memory result set (ADR-0002).</summary>
public static class PageRequest
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static PagedList<T> Apply<T>(IReadOnlyList<T> items, int page, int pageSize)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageSize, MaxPageSize);

        var skip = (long)(page - 1) * pageSize;
        IReadOnlyList<T> slice = skip >= items.Count
            ? []
            : [.. items.Skip((int)skip).Take(pageSize)];

        return new PagedList<T>(slice, page, pageSize, items.Count);
    }
}
