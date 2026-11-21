using Aggregator.Application.Sources;
using NSubstitute;

namespace Aggregator.Application.UnitTests.Sources;

public sealed class DataSourceRegistryTests
{
    [Fact]
    public void All_ShouldKeepRegistrationOrder_WhenSourcesAreRegistered()
    {
        var registry = new DataSourceRegistry([Source("weather"), Source("news"), Source("repositories")]);

        registry.All.Select(s => s.Name).ShouldBe(["weather", "news", "repositories"]);
    }

    [Theory]
    [InlineData("news")]
    [InlineData("NEWS")]
    [InlineData("News")]
    public void TryGet_ShouldFindSource_WhenNameDiffersOnlyInCase(string name)
    {
        var news = Source("news");
        var registry = new DataSourceRegistry([Source("weather"), news]);

        registry.TryGet(name, out var found).ShouldBeTrue();
        found.ShouldBeSameAs(news);
    }

    [Fact]
    public void TryGet_ShouldReturnFalse_WhenNameIsUnknown()
    {
        var registry = new DataSourceRegistry([Source("news")]);

        registry.TryGet("sports", out var found).ShouldBeFalse();
        found.ShouldBeNull();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenTwoSourcesShareANameIgnoringCase()
    {
        var exception = Should.Throw<InvalidOperationException>(() => new DataSourceRegistry([Source("news"), Source("News")]));

        exception.Message.ShouldContain("news");
    }

    [Fact]
    public void All_ShouldBeEmpty_WhenNoSourceIsRegistered() =>
        new DataSourceRegistry([]).All.ShouldBeEmpty();

    private static IDataSource Source(string name)
    {
        var source = Substitute.For<IDataSource>();
        source.Name.Returns(name);
        return source;
    }
}
