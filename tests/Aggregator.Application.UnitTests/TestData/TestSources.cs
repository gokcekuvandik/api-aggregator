using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using NSubstitute;

namespace Aggregator.Application.UnitTests.TestData;

/// <summary>Substitute sources whose single category is their own name.</summary>
internal static class TestSources
{
    public static IDataSource Returning(string name, SourceResult result, SearchFields usedFields = SearchFields.All)
    {
        var source = Substitute.For<IDataSource>();
        source.Name.Returns(name);
        source.Categories.Returns([name]);
        source.UsedFields.Returns(usedFields);
        source.FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>()).Returns(result);
        return source;
    }

    /// <summary>A source whose fetch throws, standing in for a bug in an adapter.</summary>
    public static IDataSource Throwing(string name, Exception exception)
    {
        var source = Substitute.For<IDataSource>();
        source.Name.Returns(name);
        source.Categories.Returns([name]);
        source.UsedFields.Returns(SearchFields.All);
        source.FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>()).Returns<SourceResult>(_ => throw exception);
        return source;
    }

    /// <summary>A source whose fetch only ends when its token is cancelled.</summary>
    public static IDataSource Hanging(string name)
    {
        var source = Substitute.For<IDataSource>();
        source.Name.Returns(name);
        source.Categories.Returns([name]);
        source.UsedFields.Returns(SearchFields.All);
        source.FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>()).Returns(call => WaitForCancellationAsync(call.Arg<CancellationToken>()));
        return source;
    }

    private static async Task<SourceResult> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("Unreachable: the delay only ends by cancellation.");
    }
}
