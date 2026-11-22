namespace Aggregator.Api.Contracts;

/// <summary>Body of <c>GET /api/v1/statistics</c>.</summary>
public sealed record StatisticsResponse(IReadOnlyList<SourceStatisticsResponse> Items);
