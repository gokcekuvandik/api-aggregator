using Aggregator.Application.Aggregation;
using Aggregator.Domain.Items;

namespace Aggregator.Infrastructure.Sources.Relevance;

/// <summary>GitHub's search score and the repository's star count.</summary>
internal sealed record RepositorySignal(double SearchScore, int Stars);

/// <summary>
/// Blends the search score and the star count equally, each scaled to the best in the batch.
/// Stars are compared on a log scale so one very popular repository does not flatten the rest.
/// </summary>
internal sealed class RepositoryRelevanceScorer : IRelevanceScorer<RepositorySignal>
{
    private const double SearchWeight = 0.5;
    private const double StarsWeight = 0.5;

    public IReadOnlyList<RelevanceScore> Score(IReadOnlyList<RepositorySignal> signals, AggregationQuery query)
    {
        ArgumentNullException.ThrowIfNull(signals);

        var searchScores = signals.Select(signal => Math.Max(signal.SearchScore, 0)).ToList();
        var starScores = signals.Select(signal => Math.Log(1 + Math.Max(signal.Stars, 0))).ToList();
        var maxSearch = searchScores.DefaultIfEmpty().Max();
        var maxStars = starScores.DefaultIfEmpty().Max();

        return [.. searchScores.Zip(starScores, (search, stars) =>
            RelevanceScore.Clamp((SearchWeight * Ratio(search, maxSearch)) + (StarsWeight * Ratio(stars, maxStars))))];
    }

    private static double Ratio(double value, double max) => max > 0 ? value / max : 0;
}
