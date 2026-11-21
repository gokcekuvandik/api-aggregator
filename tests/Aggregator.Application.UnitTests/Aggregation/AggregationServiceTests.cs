using Aggregator.Application.Aggregation;
using Aggregator.Application.Aggregation.Sorting;
using Aggregator.Application.Paging;
using Aggregator.Application.Sources;
using Aggregator.Application.UnitTests.TestData;
using NSubstitute;
using SortDirection = Aggregator.Application.Aggregation.Sorting.SortDirection;

namespace Aggregator.Application.UnitTests.Aggregation;

public sealed class AggregationServiceTests
{
    private static readonly AggregationQuery DefaultQuery = new(Page: 1, PageSize: PageRequest.DefaultPageSize);

    [Fact]
    public async Task AggregateAsync_ShouldMergeItemsFromEverySource_WhenSourcesReturnData()
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

    [Fact]
    public async Task StreamAsync_ShouldYieldEachSourceAsItCompletes_WhenSourcesFinishAtDifferentTimes()
    {
        var slow = new TaskCompletionSource<SourceResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Substitute.For<IDataSource>();
        first.Name.Returns("slow");
        first.FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>()).Returns(slow.Task);
        var service = Service(first, Source("fast", new SourceSuccess([])));
        var names = new List<string>();

        await foreach (var update in service.StreamAsync(DefaultQuery, TestContext.Current.CancellationToken))
        {
            names.Add(update.Report.Name);
            if (update.Report.Name == "fast")
            {
                slow.SetResult(new SourceSuccess([]));
            }
        }

        names.ShouldBe(["fast", "slow"]);
    }

    [Fact]
    public async Task StreamAsync_ShouldFilterAndSortEachSourceWithoutPaging_WhenQueryHasCriteria()
    {
        var from = TestItems.PublishedAt.AddHours(-1);
        var service = Service(Source("news", new SourceSuccess(
        [
            TestItems.Item("low", relevance: 0.1),
            TestItems.Item("too-old", publishedAt: from.AddTicks(-1), relevance: 1),
            TestItems.Item("high", relevance: 0.9),
            TestItems.Item("mid", relevance: 0.5),
        ])));
        var query = new AggregationQuery(Page: 2, PageSize: 1) { From = from };

        var updates = await ToListAsync(service.StreamAsync(query, TestContext.Current.CancellationToken));

        var update = updates.ShouldHaveSingleItem();
        update.Items.Select(i => i.Id).ShouldBe(["high", "mid", "low"]);
        update.Report.Result.ShouldBeOfType<SourceSuccess>();
    }

    [Fact]
    public async Task StreamAsync_ShouldCancelPendingFetches_WhenConsumerStopsEarly()
    {
        var pendingToken = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow = Substitute.For<IDataSource>();
        slow.Name.Returns("slow");
        slow.FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var token = call.Arg<CancellationToken>();
            pendingToken.SetResult(token);
            return Task.Delay(Timeout.InfiniteTimeSpan, token).ContinueWith<SourceResult>(_ => new SourceSuccess([]), TaskScheduler.Default);
        });
        var service = Service(slow, Source("fast", new SourceSuccess([])));

        await foreach (var update in service.StreamAsync(DefaultQuery, TestContext.Current.CancellationToken))
        {
            update.Report.Name.ShouldBe("fast");
            break;
        }

        (await pendingToken.Task).IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task StreamAsync_ShouldThrow_WhenQueryIsInvalid()
    {
        var service = Service(Source("news", new SourceSuccess([])));

        await Should.ThrowAsync<ArgumentException>(() =>
            ToListAsync(service.StreamAsync(DefaultQuery with { Sources = ["nope"] }, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AggregateAsync_ShouldCallOnlyNamedSources_WhenSourcesAreGiven()
    {
        var news = Source("news", new SourceSuccess([]));
        var weather = Source("weather", new SourceSuccess([]));
        var query = DefaultQuery with { Sources = ["WEATHER"] };

        var result = await Service(news, weather).AggregateAsync(query, TestContext.Current.CancellationToken);

        result.Sources.Select(s => s.Name).ShouldBe(["weather"]);
        await news.DidNotReceive().FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AggregateAsync_ShouldSkipSourcesWithoutCategory_WhenCategoryIsGiven()
    {
        var news = Source("news", new SourceSuccess([]));
        var weather = Source("weather", new SourceSuccess([TestItems.Item("w", source: "weather", category: "weather")]));
        var query = DefaultQuery with { Category = "Weather" };

        var result = await Service(news, weather).AggregateAsync(query, TestContext.Current.CancellationToken);

        result.Items.Items.Select(i => i.Id).ShouldBe(["w"]);
        await news.DidNotReceive().FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AggregateAsync_ShouldFilterAndSortBeforePaging_WhenQueryHasCriteria()
    {
        var from = TestItems.PublishedAt.AddHours(-1);
        var service = Service(Source("news", new SourceSuccess(
        [
            TestItems.Item("low", relevance: 0.1),
            TestItems.Item("too-old", publishedAt: from.AddTicks(-1), relevance: 1),
            TestItems.Item("high", relevance: 0.9),
            TestItems.Item("mid", relevance: 0.5),
        ])));
        var query = new AggregationQuery(Page: 1, PageSize: 2) { From = from, SortBy = ItemSortField.Relevance, SortOrder = SortDirection.Descending };

        var result = await service.AggregateAsync(query, TestContext.Current.CancellationToken);

        result.Items.Items.Select(i => i.Id).ShouldBe(["high", "mid"]);
        result.Items.TotalCount.ShouldBe(3);
    }

    [Fact]
    public void Validate_ShouldReturnNoErrors_WhenQueryIsValid()
    {
        var query = DefaultQuery with { Sources = ["news"], Category = "NEWS" };

        Service(Source("news", new SourceSuccess([]))).Validate(query).ShouldBeEmpty();
    }

    [Fact]
    public void Validate_ShouldRejectSource_WhenNameIsUnknown()
    {
        var errors = Service(Source("news", new SourceSuccess([]))).Validate(DefaultQuery with { Sources = ["news", "nope"] });

        errors.ShouldHaveSingleItem().Key.ShouldBe(nameof(AggregationQuery.Sources));
        errors[nameof(AggregationQuery.Sources)].ShouldBe(["Unknown source 'nope'. Known sources: news."]);
    }

    [Fact]
    public void Validate_ShouldRejectCategory_WhenNoSourceServesIt()
    {
        var errors = Service(Source("news", new SourceSuccess([]))).Validate(DefaultQuery with { Category = "sports" });

        errors.ShouldHaveSingleItem().Key.ShouldBe(nameof(AggregationQuery.Category));
        errors[nameof(AggregationQuery.Category)].ShouldBe(["Unknown category 'sports'. Known categories: news."]);
    }

    [Fact]
    public void Validate_ShouldRejectCategory_WhenSelectedSourcesDoNotServeIt()
    {
        var service = Service(Source("news", new SourceSuccess([])), Source("weather", new SourceSuccess([])));

        var errors = service.Validate(DefaultQuery with { Sources = ["weather"], Category = "news" });

        errors[nameof(AggregationQuery.Category)].ShouldBe(["None of the selected sources serves category 'news'."]);
    }

    private static AggregationService Service(params IDataSource[] sources) => new(new DataSourceRegistry(sources));

    private static async Task<List<SourceUpdate>> ToListAsync(IAsyncEnumerable<SourceUpdate> updates)
    {
        var list = new List<SourceUpdate>();
        await foreach (var update in updates)
        {
            list.Add(update);
        }

        return list;
    }

    private static IDataSource Source(string name, SourceResult result) => TestSources.Returning(name, result);
}
