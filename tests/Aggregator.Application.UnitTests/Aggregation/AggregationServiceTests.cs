using Aggregator.Application.Aggregation;
using Aggregator.Application.Paging;
using Aggregator.Application.Sources;
using Aggregator.Application.UnitTests.TestData;
using NSubstitute;

namespace Aggregator.Application.UnitTests.Aggregation;

public sealed class AggregationServiceTests
{
    private static readonly AggregationQuery DefaultQuery = new(Page: 1, PageSize: PageRequest.DefaultPageSize);

    [Fact]
    public async Task AggregateAsync_ShouldMergeItemsInSourceOrder_WhenSourcesReturnData()
    {
        var service = Service(
            Source("news", new SourceSuccess(TestItems.Many(2, "news"))),
            Source("weather", new SourceFallback(TestItems.Many(1, "weather"), TimeSpan.FromMinutes(1))));

        var result = await service.AggregateAsync(DefaultQuery, TestContext.Current.CancellationToken);

        result.Items.Items.Select(i => i.Id).ShouldBe(["news-1", "news-2", "weather-1"]);
        result.Items.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task AggregateAsync_ShouldReportEverySource_WhenOneFails()
    {
        var failure = new SourceFailure("timeout");
        var service = Service(
            Source("news", new SourceSuccess(TestItems.Many(1, "news"))),
            Source("weather", failure));

        var result = await service.AggregateAsync(DefaultQuery, TestContext.Current.CancellationToken);

        result.Sources.Select(s => s.Name).ShouldBe(["news", "weather"]);
        result.Sources[1].Result.ShouldBe(failure);
        result.Sources.ShouldAllBe(s => s.Duration >= TimeSpan.Zero);
        result.Items.Items.ShouldAllBe(i => i.Source == "news");
    }

    [Fact]
    public async Task AggregateAsync_ShouldPageMergedItems_WhenQueryRequestsAPage()
    {
        var service = Service(Source("news", new SourceSuccess(TestItems.Many(5, "news"))));

        var result = await service.AggregateAsync(new AggregationQuery(Page: 2, PageSize: 2), TestContext.Current.CancellationToken);

        result.Items.Items.Select(i => i.Id).ShouldBe(["news-3", "news-4"]);
        result.Items.TotalCount.ShouldBe(5);
    }

    [Fact]
    public async Task AggregateAsync_ShouldPassQueryAndToken_WhenCallingSources()
    {
        var source = Source("news", new SourceSuccess([]));
        using var cts = new CancellationTokenSource();
        var query = new AggregationQuery(Page: 1, PageSize: 5);

        await Service(source).AggregateAsync(query, cts.Token);

        await source.Received(1).FetchAsync(query, cts.Token);
    }

    [Fact]
    public async Task AggregateAsync_ShouldStartAllSources_WhenFirstSourceHasNotCompleted()
    {
        var slow = new TaskCompletionSource<SourceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Substitute.For<IDataSource>();
        first.Name.Returns("slow");
        first.FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>()).Returns(slow.Task);
        var second = Source("fast", new SourceSuccess([]));

        var pending = Service(first, second).AggregateAsync(DefaultQuery, TestContext.Current.CancellationToken);

        await second.Received(1).FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>());
        pending.IsCompleted.ShouldBeFalse();
        slow.SetResult(new SourceSuccess([]));
        (await pending).Sources.Count.ShouldBe(2);
    }

    private static AggregationService Service(params IDataSource[] sources) => new(new DataSourceRegistry(sources));

    private static IDataSource Source(string name, SourceResult result)
    {
        var source = Substitute.For<IDataSource>();
        source.Name.Returns(name);
        source.Categories.Returns([name]);
        source.FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>()).Returns(result);
        return source;
    }
}
