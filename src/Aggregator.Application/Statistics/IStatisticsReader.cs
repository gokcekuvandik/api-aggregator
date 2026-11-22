namespace Aggregator.Application.Statistics;

/// <summary>Read side of the request statistics store.</summary>
public interface IStatisticsReader
{
    /// <summary>An immutable copy of the current statistics, one entry per external API.</summary>
    IReadOnlyList<ApiStatisticsResult> GetSnapshots();
}
