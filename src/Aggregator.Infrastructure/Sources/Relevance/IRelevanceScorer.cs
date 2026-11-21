using Aggregator.Application.Aggregation;
using Aggregator.Domain.Items;

namespace Aggregator.Infrastructure.Sources.Relevance;

/// <summary>
/// Turns one source's raw ranking signals into 0–1 scores, one per signal and in the same order.
/// Scores are relative to the batch, so they are a heuristic rather than comparable measurements.
/// </summary>
internal interface IRelevanceScorer<TSignal>
{
    IReadOnlyList<RelevanceScore> Score(IReadOnlyList<TSignal> signals, AggregationQuery query);
}
