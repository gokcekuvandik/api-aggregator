namespace Aggregator.Api.Contracts;

public sealed record NewsDetailsResponse(string? Author, int? Points, string? Publisher) : ItemDetailsResponse;
