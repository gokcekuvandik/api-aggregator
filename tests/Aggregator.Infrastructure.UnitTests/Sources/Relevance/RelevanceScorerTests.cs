using Aggregator.Application.Aggregation;
using Aggregator.Infrastructure.Sources.Relevance;
using Aggregator.Infrastructure.UnitTests.TestDoubles;

namespace Aggregator.Infrastructure.UnitTests.Sources.Relevance;

public sealed class RelevanceScorerTests
{
    private static readonly AggregationQuery Query = SourceTestHost.Query;
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RankScorer_ShouldScoreFirstHighest_WhenResultsAreInRankOrder()
    {
        var scores = new RankRelevanceScorer().Score(["a", "b", "c", "d"], Query);

        scores.Select(score => score.Value).ShouldBe([1.0, 0.75, 0.5, 0.25]);
    }

    [Fact]
    public void RankScorer_ShouldScoreOne_WhenThereIsOneResult() =>
        new RankRelevanceScorer().Score(["only"], Query).ShouldHaveSingleItem().Value.ShouldBe(1.0);

    [Fact]
    public void RankScorer_ShouldReturnNothing_WhenThereAreNoResults() =>
        new RankRelevanceScorer().Score(Array.Empty<string>(), Query).ShouldBeEmpty();

    [Fact]
    public void RepositoryScorer_ShouldBlendSearchScoreAndStars_WhenBothVary()
    {
        RepositorySignal[] signals = [new(SearchScore: 10, Stars: 999), new(SearchScore: 5, Stars: 0), new(SearchScore: 0, Stars: 999)];

        var scores = new RepositoryRelevanceScorer().Score(signals, Query);

        scores.Select(score => Math.Round(score.Value, 6)).ShouldBe([1.0, 0.25, 0.5]);
    }

    [Fact]
    public void RepositoryScorer_ShouldUseLogarithmicStars_WhenStarsDifferByOrdersOfMagnitude()
    {
        RepositorySignal[] signals = [new(1, 99_999), new(1, 99)];

        var scores = new RepositoryRelevanceScorer().Score(signals, Query);

        scores[1].Value.ShouldBe(0.5 + (0.5 * Math.Log(100) / Math.Log(100_000)), tolerance: 1e-9);
    }

    [Fact]
    public void RepositoryScorer_ShouldScoreZero_WhenEverySignalIsZero() =>
        new RepositoryRelevanceScorer().Score([new RepositorySignal(0, 0), new RepositorySignal(0, 0)], Query)
            .ShouldAllBe(score => score.Value == 0);

    [Fact]
    public void RepositoryScorer_ShouldTreatNegativeValuesAsZero_WhenUpstreamSendsThem() =>
        new RepositoryRelevanceScorer().Score([new RepositorySignal(-5, -1)], Query).ShouldHaveSingleItem().Value.ShouldBe(0);

    public static TheoryData<string, double, double> WeatherDistances => new()
    {
        { "now", 0, 1.0 },
        { "one day ahead", 24, 0.8 },
        { "one day ago", -24, 0.8 },
        { "five days ahead", 120, 0.0 },
        { "beyond the horizon", 240, 0.0 },
    };

    [Theory]
    [MemberData(nameof(WeatherDistances))]
    public void WeatherScorer_ShouldScoreByClosenessToNow_WhenNoStartIsGiven(string scenario, double hoursFromNow, double expected)
    {
        var scorer = new WeatherRelevanceScorer(new FixedTimeProvider(Now));

        var score = scorer.Score([Now.AddHours(hoursFromNow)], Query).ShouldHaveSingleItem();

        score.Value.ShouldBe(expected, tolerance: 1e-9, customMessage: scenario);
    }

    [Fact]
    public void WeatherScorer_ShouldScoreByClosenessToFrom_WhenStartIsGiven()
    {
        var scorer = new WeatherRelevanceScorer(new FixedTimeProvider(Now));
        var from = Now.AddDays(2);

        var scores = scorer.Score([from, Now], Query with { From = from });

        scores.Select(score => score.Value).ShouldBe([1.0, 0.6], tolerance: 1e-9);
    }
}
