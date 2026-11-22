namespace Aggregator.Api.Contracts;

public sealed record PagingResponse(int Page, int PageSize, int TotalCount);
