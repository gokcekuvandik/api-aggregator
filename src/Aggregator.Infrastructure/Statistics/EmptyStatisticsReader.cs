using Aggregator.Application.Statistics;

namespace Aggregator.Infrastructure.Statistics;

/// <summary>Returns no statistics until a real store exists.</summary>
internal sealed class EmptyStatisticsReader : IStatisticsReader
{
    public IReadOnlyList<ApiStatisticsSnapshot> GetSnapshots() => [];
}
