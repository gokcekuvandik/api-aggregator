using Aggregator.Application.Aggregation;
using Aggregator.Domain.Items;

namespace Aggregator.Infrastructure.Sources.Relevance;

/// <summary>Scores search results by position: the first of n gets 1, the last gets 1/n. The content is ignored.</summary>
internal sealed class RankRelevanceScorer : IRelevanceScorer<object>
{
    public IReadOnlyList<RelevanceScore> Score(IReadOnlyList<object> signals, AggregationQuery query)
    {
        ArgumentNullException.ThrowIfNull(signals);
        var count = signals.Count;
        return [.. Enumerable.Range(0, count).Select(rank => RelevanceScore.Clamp(1 - ((double)rank / count)))];
    }
}
