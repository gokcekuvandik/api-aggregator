namespace Aggregator.Api.Contracts;

/// <summary>Body of <c>GET /api/v1/sources</c>.</summary>
public sealed record SourcesResponse(IReadOnlyList<SourceInfoResponse> Items);
