using Aggregator.Application.Aggregation;

namespace Aggregator.Application.UnitTests.Aggregation;

public sealed class AggregationQueryTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  Paris ", "Paris")]
    public void TextFields_ShouldBeTrimmedAndBlankBecomesNull_WhenSet(string? value, string? expected)
    {
        var query = new AggregationQuery(Page: 1, PageSize: 20) { Text = value, City = value, Category = value };

        query.Text.ShouldBe(expected);
        query.City.ShouldBe(expected);
        query.Category.ShouldBe(expected);
    }
}
