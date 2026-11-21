using Aggregator.Application.Aggregation;
using Aggregator.Application.Paging;
using Aggregator.Application.Sources;
using Aggregator.Application.UnitTests.TestData;

namespace Aggregator.Application.UnitTests.Aggregation;

/// <summary>ADR-0002 / ADR-0014 decision table: source states → is any data available?</summary>
public sealed class AggregationResultTests
{
    private static readonly SourceResult Success = new SourceSuccess(TestItems.Many(1));
    private static readonly SourceResult Cached = new SourceCached(TestItems.Many(1));
    private static readonly SourceResult Fallback = new SourceFallback(TestItems.Many(1), Age: TimeSpan.FromMinutes(3));
    private static readonly SourceResult Failed = new SourceFailure("boom");

    public static TheoryData<string, SourceResult[], bool> DecisionTable => new()
    {
        { "all success", [Success, Success], true },
        { "success + failed", [Success, Failed], true },
        { "cached only", [Cached], true },
        { "fallback only", [Fallback], true },
        { "fallback + failed", [Fallback, Failed], true },
        { "success with no items", [new SourceSuccess([])], true },
        { "all failed", [Failed, Failed], false },
        { "no sources", [], false },
    };

    [Theory]
    [MemberData(nameof(DecisionTable))]
    public void HasData_ShouldFollowDecisionTable_WhenSourcesHaveGivenStates(string scenario, SourceResult[] results, bool expected)
    {
        var reports = results.Select((r, i) => new SourceReport($"source-{i}", r, TimeSpan.FromMilliseconds(10))).ToList();
        var result = new AggregationResult(PageRequest.Apply(TestItems.Many(0), 1, 10), reports);

        result.HasData.ShouldBe(expected, scenario);
    }

    [Fact]
    public void Items_ShouldBeEmpty_WhenSourceFailed() =>
        Failed.Items.ShouldBeEmpty();
}
