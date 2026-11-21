using Aggregator.Domain.Items;

namespace Aggregator.Domain.UnitTests.Items;

public sealed class RelevanceScoreTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void From_ShouldKeepValue_WhenValueIsWithinRange(double value)
    {
        var score = RelevanceScore.From(value);

        score.Value.ShouldBe(value);
    }

    [Theory]
    [InlineData(-0.000001)]
    [InlineData(1.000001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void From_ShouldThrow_WhenValueIsOutsideRange(double value)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RelevanceScore.From(value));
    }

    [Theory]
    [InlineData(-3.0, 0.0)]
    [InlineData(0.25, 0.25)]
    [InlineData(7.0, 1.0)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    [InlineData(double.NegativeInfinity, 0.0)]
    public void Clamp_ShouldForceValueIntoRange_WhenValueIsAnyDouble(double value, double expected)
    {
        var score = RelevanceScore.Clamp(value);

        score.Value.ShouldBe(expected);
    }

    [Fact]
    public void Default_ShouldBeZero_WhenNotSet()
    {
        default(RelevanceScore).ShouldBe(RelevanceScore.None);
        RelevanceScore.None.Value.ShouldBe(0.0);
    }

    [Fact]
    public void CompareTo_ShouldOrderByValue_WhenScoresDiffer()
    {
        RelevanceScore[] scores = [RelevanceScore.From(0.7), RelevanceScore.None, RelevanceScore.From(1)];

        var ordered = scores.Order().Select(score => score.Value);

        ordered.ShouldBe([0.0, 0.7, 1.0]);
    }

    [Theory]
    [InlineData(0.2, 0.8, true, false, true, false)]
    [InlineData(0.8, 0.2, false, true, false, true)]
    [InlineData(0.5, 0.5, false, false, true, true)]
    public void ComparisonOperators_ShouldAgreeWithCompareTo_WhenScoresAreCompared(
        double left, double right, bool less, bool greater, bool lessOrEqual, bool greaterOrEqual)
    {
        var a = RelevanceScore.From(left);
        var b = RelevanceScore.From(right);

        (a < b).ShouldBe(less);
        (a > b).ShouldBe(greater);
        (a <= b).ShouldBe(lessOrEqual);
        (a >= b).ShouldBe(greaterOrEqual);
    }
}
