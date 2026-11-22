using System.Collections.Concurrent;
using Aggregator.Application.Statistics;
using Aggregator.Domain.Statistics;

namespace Aggregator.Infrastructure.Statistics;

/// <summary>In-memory request statistics for this instance, one <see cref="ApiStatistics"/> per external API, created on first use.</summary>
internal sealed class StatisticsStore : IStatisticsReader
{
    private readonly ConcurrentDictionary<UpstreamApi, ApiStatistics> _apis = new();
    private readonly TimeProvider _time;
    private readonly PerformanceThresholds _thresholds;

    public StatisticsStore(TimeProvider time, PerformanceThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(thresholds);

        _time = time;
        _thresholds = thresholds;
    }

    public void RecordRequest(UpstreamApi api, TimeSpan duration, RequestOutcome outcome, int attempts) =>
        For(api).RecordRequest(duration, outcome, attempts, _time.GetUtcNow());

    public void RecordCacheHit(UpstreamApi api) => For(api).RecordCacheHit();

    /// <summary>Ordered by source, with the primary provider before secondaries.</summary>
    public IReadOnlyList<ApiStatisticsSnapshot> GetSnapshots()
    {
        var now = _time.GetUtcNow();
        return
        [
            .. _apis.Values
                .Select(statistics => statistics.Snapshot(now))
                .OrderBy(snapshot => snapshot.Api.Source, StringComparer.Ordinal)
                .ThenBy(snapshot => snapshot.Api.Provider, StringComparer.Ordinal),
        ];
    }

    private ApiStatistics For(UpstreamApi api)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(api.Source, nameof(api));
        return _apis.GetOrAdd(api, static (key, limits) => new ApiStatistics(key, limits), _thresholds);
    }
}
