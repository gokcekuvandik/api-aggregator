using Aggregator.Application.Statistics;

namespace Aggregator.Api.Contracts;

/// <summary>Body of <c>GET /api/v1/statistics</c>.</summary>
public sealed record StatisticsResponse(IReadOnlyList<SourceStatisticsResponse> Items);

public sealed record SourceStatisticsResponse(
    string Source,
    long TotalRequests,
    double AverageMs,
    long CacheHits,
    long Failures)
{
    public static SourceStatisticsResponse From(ApiStatisticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(snapshot.Source, snapshot.TotalRequests, snapshot.AverageDuration.TotalMilliseconds, snapshot.CacheHits, snapshot.Failures);
    }
}
