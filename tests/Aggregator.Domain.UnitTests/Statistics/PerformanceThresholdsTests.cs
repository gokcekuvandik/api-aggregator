using Aggregator.Domain.Statistics;

namespace Aggregator.Domain.UnitTests.Statistics;

public sealed class PerformanceThresholdsTests
{
    [Theory]
    [InlineData(0, PerformanceBucket.Fast)]
    [InlineData(99, PerformanceBucket.Fast)]
    [InlineData(99.999, PerformanceBucket.Fast)]
    [InlineData(100, PerformanceBucket.Average)]
    [InlineData(150, PerformanceBucket.Average)]
    [InlineData(200, PerformanceBucket.Average)]
    [InlineData(200.001, PerformanceBucket.Slow)]
    [InlineData(201, PerformanceBucket.Slow)]
    [InlineData(10_000, PerformanceBucket.Slow)]
    public void Classify_ShouldPickTheBucket_WhenDurationIsAroundTheDefaultBoundaries(double milliseconds, PerformanceBucket expected)
    {
        var bucket = PerformanceThresholds.Default.Classify(TimeSpan.FromMilliseconds(milliseconds));

        bucket.ShouldBe(expected);
    }

    [Theory]
    [InlineData(49, PerformanceBucket.Fast)]
    [InlineData(50, PerformanceBucket.Average)]
    [InlineData(500, PerformanceBucket.Average)]
    [InlineData(501, PerformanceBucket.Slow)]
    public void Classify_ShouldUseTheGivenBoundaries_WhenThresholdsAreCustom(double milliseconds, PerformanceBucket expected)
    {
        var thresholds = new PerformanceThresholds(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(500));

        thresholds.Classify(TimeSpan.FromMilliseconds(milliseconds)).ShouldBe(expected);
    }

    [Fact]
    public void Classify_ShouldLeaveOnlyTheBoundaryAsAverage_WhenBothBoundariesAreEqual()
    {
        var thresholds = new PerformanceThresholds(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

        thresholds.Classify(TimeSpan.FromMilliseconds(99)).ShouldBe(PerformanceBucket.Fast);
        thresholds.Classify(TimeSpan.FromMilliseconds(100)).ShouldBe(PerformanceBucket.Average);
        thresholds.Classify(TimeSpan.FromMilliseconds(101)).ShouldBe(PerformanceBucket.Slow);
    }

    [Fact]
    public void Default_ShouldBeOneHundredAndTwoHundredMilliseconds_WhenNothingIsConfigured()
    {
        PerformanceThresholds.Default.FastBelow.ShouldBe(TimeSpan.FromMilliseconds(100));
        PerformanceThresholds.Default.SlowAbove.ShouldBe(TimeSpan.FromMilliseconds(200));
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(-1, 200)]
    [InlineData(300, 200)]
    public void Constructor_ShouldThrow_WhenBoundariesAreNotPositiveAndOrdered(double fastBelow, double slowAbove)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new PerformanceThresholds(TimeSpan.FromMilliseconds(fastBelow), TimeSpan.FromMilliseconds(slowAbove)));
    }

    [Fact]
    public void Classify_ShouldThrow_WhenDurationIsNegative()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PerformanceThresholds.Default.Classify(TimeSpan.FromMilliseconds(-1)));
    }
}
