using Aggregator.Domain.Items;
using Aggregator.Infrastructure.Sources.Relevance;
using Aggregator.Infrastructure.Sources.Weather;
using Aggregator.Infrastructure.UnitTests.TestDoubles;

namespace Aggregator.Infrastructure.UnitTests.Sources.Weather;

public sealed class WeatherItemMapperTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly WeatherRelevanceScorer Scorer = new(new FixedTimeProvider(Noon));

    [Fact]
    public void ToItems_ShouldDescribeReading_WhenBothTemperaturesAreKnown()
    {
        var item = ToItems(new WeatherReading(Noon, 9.6, 18.4, "overcast")).ShouldHaveSingleItem();

        item.Id.ShouldBe("weather:Athens:2026-11-21T12:00Z");
        item.Source.ShouldBe("weather");
        item.Category.ShouldBe("weather");
        item.Title.ShouldBe("Athens: overcast, 10 to 18 °C");
        item.PublishedAt.ShouldBe(Noon);
        item.Url.ShouldBeNull();
        item.Details.ShouldBe(new WeatherDetails("Athens", MinTemperature: 9.6, MaxTemperature: 18.4, Conditions: "overcast"));
    }

    [Fact]
    public void ToItems_ShouldScoreByClosenessToNow_WhenReadingsSpanDays()
    {
        var items = ToItems(new WeatherReading(Noon, null, null, "rain"), new WeatherReading(Noon.AddDays(1), null, null, "rain"));

        items.Select(item => item.Relevance.Value).ShouldBe([1.0, 0.8], tolerance: 1e-9);
    }

    [Theory]
    [InlineData(9.6, null, "Athens: rain, min 10 °C")]
    [InlineData(null, 18.4, "Athens: rain, max 18 °C")]
    [InlineData(null, null, "Athens: rain")]
    [InlineData(-1.6, 0.2, "Athens: rain, -2 to 0 °C")]
    public void ToItems_ShouldOmitMissingTemperatures_WhenSourceLeavesThemOut(double? min, double? max, string expected) =>
        ToItems(new WeatherReading(Noon, min, max, "rain")).ShouldHaveSingleItem().Title.ShouldBe(expected);

    [Fact]
    public void ToItems_ShouldReturnEmpty_WhenThereAreNoReadings() =>
        ToItems().ShouldBeEmpty();

    private static IReadOnlyList<AggregatedItem> ToItems(params WeatherReading[] readings) =>
        WeatherItemMapper.ToItems("weather", "Athens", readings, Scorer, SourceTestHost.Query);
}
