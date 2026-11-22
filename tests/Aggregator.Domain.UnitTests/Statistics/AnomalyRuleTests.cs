using Aggregator.Domain.Statistics;

namespace Aggregator.Domain.UnitTests.Statistics;

public sealed class AnomalyRuleTests
{
    private static readonly TimeSpan Overall = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void Evaluate_ShouldBeNormal_WhenTheWindowIsExactlyOneAndAHalfTimesTheOverallAverage()
    {
        var verdict = AnomalyRule.Default.Evaluate(TimeSpan.FromMilliseconds(150), Overall, windowCount: 10);

        verdict.ShouldBe(AnomalyVerdict.Normal);
    }

    [Fact]
    public void Evaluate_ShouldBeAnomalous_WhenTheWindowIsJustAboveOneAndAHalfTimesTheOverallAverage()
    {
        var verdict = AnomalyRule.Default.Evaluate(TimeSpan.FromMilliseconds(150) + TimeSpan.FromTicks(1), Overall, windowCount: 10);

        verdict.ShouldBe(AnomalyVerdict.Anomalous);
    }

    [Fact]
    public void Evaluate_ShouldReportInsufficientSamples_WhenTheWindowHasOneSampleFewerThanTheMinimum()
    {
        var verdict = AnomalyRule.Default.Evaluate(TimeSpan.FromSeconds(5), Overall, windowCount: 9);

        verdict.ShouldBe(AnomalyVerdict.InsufficientSamples);
    }

    [Fact]
    public void Evaluate_ShouldReportInsufficientSamples_WhenTheWindowIsEmptyAndLooksFast()
    {
        AnomalyRule.Default.Evaluate(TimeSpan.Zero, Overall, windowCount: 0).ShouldBe(AnomalyVerdict.InsufficientSamples);
    }

    [Fact]
    public void Evaluate_ShouldBeAnomalous_WhenTheWindowHasExactlyTheMinimumSamples()
    {
        var verdict = AnomalyRule.Default.Evaluate(TimeSpan.FromSeconds(5), Overall, windowCount: 10);

        verdict.ShouldBe(AnomalyVerdict.Anomalous);
    }

    [Fact]
    public void Evaluate_ShouldBeNormal_WhenTheWindowIsFasterThanTheOverallAverage()
    {
        AnomalyRule.Default.Evaluate(TimeSpan.FromMilliseconds(20), Overall, windowCount: 100).ShouldBe(AnomalyVerdict.Normal);
    }

    [Fact]
    public void Evaluate_ShouldBeAnomalous_WhenTheOverallAverageIsZeroAndTheWindowIsNot()
    {
        AnomalyRule.Default.Evaluate(TimeSpan.FromTicks(1), TimeSpan.Zero, windowCount: 10).ShouldBe(AnomalyVerdict.Anomalous);
    }

    [Theory]
    [InlineData(0.25, 3, 125, 3, AnomalyVerdict.Normal)]
    [InlineData(0.25, 3, 126, 3, AnomalyVerdict.Anomalous)]
    [InlineData(0.25, 3, 126, 2, AnomalyVerdict.InsufficientSamples)]
    [InlineData(2, 1, 300, 1, AnomalyVerdict.Normal)]
    [InlineData(2, 1, 301, 1, AnomalyVerdict.Anomalous)]
    public void Evaluate_ShouldUseTheGivenValues_WhenTheRuleIsCustom(double threshold, int minSamples, double windowMs, long count, AnomalyVerdict expected)
    {
        var rule = new AnomalyRule(threshold, minSamples);

        rule.Evaluate(TimeSpan.FromMilliseconds(windowMs), Overall, count).ShouldBe(expected);
    }

    [Fact]
    public void Default_ShouldBeHalfAboveTheAverageWithTenSamples_WhenNothingIsConfigured()
    {
        AnomalyRule.Default.Threshold.ShouldBe(0.5);
        AnomalyRule.Default.MinSamples.ShouldBe(10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Constructor_ShouldThrow_WhenTheThresholdIsNotPositiveAndFinite(double threshold)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new AnomalyRule(threshold, 10));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_ShouldThrow_WhenTheMinimumSamplesIsBelowOne(int minSamples)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new AnomalyRule(0.5, minSamples));
    }

    [Fact]
    public void Evaluate_ShouldThrow_WhenAnAverageOrTheCountIsNegative()
    {
        var negative = TimeSpan.FromTicks(-1);

        Should.Throw<ArgumentOutOfRangeException>(() => AnomalyRule.Default.Evaluate(negative, Overall, 10));
        Should.Throw<ArgumentOutOfRangeException>(() => AnomalyRule.Default.Evaluate(Overall, negative, 10));
        Should.Throw<ArgumentOutOfRangeException>(() => AnomalyRule.Default.Evaluate(Overall, Overall, -1));
    }
}
