namespace Aggregator.Api.Contracts;

public sealed record RepositoryDetailsResponse(int Stars, int Forks, string? Language) : ItemDetailsResponse;
