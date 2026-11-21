using Aggregator.Application.Statistics;

namespace Aggregator.Infrastructure.Statistics;

/// <summary>Placeholder until the in-memory statistics store of ADR-0008 exists.</summary>
internal sealed class EmptyStatisticsReader : IStatisticsReader
{
    public IReadOnlyList<ApiStatisticsSnapshot> GetSnapshots() => [];
}
