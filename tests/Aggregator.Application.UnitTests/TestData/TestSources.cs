using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using NSubstitute;

namespace Aggregator.Application.UnitTests.TestData;

/// <summary>Substitute sources whose single category is their own name.</summary>
internal static class TestSources
{
    public static IDataSource Returning(string name, SourceResult result)
    {
        var source = Substitute.For<IDataSource>();
        source.Name.Returns(name);
        source.Categories.Returns([name]);
        source.FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>()).Returns(result);
        return source;
    }
}
