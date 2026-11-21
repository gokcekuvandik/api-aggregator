using Aggregator.Application.Aggregation;
using Aggregator.Domain.Items;

namespace Aggregator.Infrastructure.Sources.Relevance;

/// <summary>
/// Scores forecast readings by how close they are to the requested start, or to now when none is given.
/// The score falls linearly to 0 at five days, the longest forecast either provider returns.
/// </summary>
internal sealed class WeatherRelevanceScorer(TimeProvider timeProvider) : IRelevanceScorer<DateTimeOffset>
{
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(5);

    public IReadOnlyList<RelevanceScore> Score(IReadOnlyList<DateTimeOffset> signals, AggregationQuery query)
    {
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(query);

        var anchor = query.From ?? timeProvider.GetUtcNow();
        return [.. signals.Select(at => RelevanceScore.Clamp(1 - ((at - anchor).Duration() / Horizon)))];
    }
}
