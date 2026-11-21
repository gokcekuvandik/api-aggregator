namespace Aggregator.Application.Paging;

/// <summary>One page of items plus the size of the whole result set.</summary>
public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
