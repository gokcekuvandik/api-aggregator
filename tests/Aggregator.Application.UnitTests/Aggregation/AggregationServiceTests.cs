using Aggregator.Application.Aggregation;
using Aggregator.Application.Aggregation.Sorting;
using Aggregator.Application.Paging;
using Aggregator.Application.Sources;
using Aggregator.Application.Telemetry;
using Aggregator.Application.UnitTests.TestData;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using NSubstitute;
using SortDirection = Aggregator.Application.Aggregation.Sorting.SortDirection;

namespace Aggregator.Application.UnitTests.Aggregation;

public sealed class AggregationServiceTests : IDisposable
{
    private static readonly AggregationQuery DefaultQuery = new(Page: 1, PageSize: PageRequest.DefaultPageSize);

    private readonly TestMeterFactory _meters = new();
    private readonly AggregationTelemetry _telemetry;
    private readonly MetricCollector<double> _durations;

    public AggregationServiceTests()
    {
        _telemetry = new AggregationTelemetry(_meters);
        _durations = new MetricCollector<double>(_meters, AggregatorDiagnostics.Name, AggregationTelemetry.AggregationDurationName);
    }

    /// <summary>What the two sources answer → the outcome an aggregation is recorded with.</summary>
    public static TheoryData<string, string, string> OutcomeTable => new()
    {
        { "success", "cached", "complete" },
        { "success", "fallback", "degraded" },
        { "success", "failure", "degraded" },
        { "fallback", "failure", "degraded" },
        { "failure", "failure", "failed" },
    };

    [Theory]
    [MemberData(nameof(OutcomeTable))]
    public async Task AggregateAsync_ShouldRecordItsDurationByOutcome_WhenEverySourceHasAnswered(string first, string second, string expected)
    {
        var service = Service(Source("news", Answer(first)), Source("weather", Answer(second)));

        await service.AggregateAsync(DefaultQuery, TestContext.Current.CancellationToken);

        var duration = _durations.GetMeasurementSnapshot().ShouldHaveSingleItem();
        duration.Value.ShouldBeGreaterThanOrEqualTo(0);
        duration.Tags.ShouldBe(new Dictionary<string, object?> { ["mode"] = "page", ["outcome"] = expected }, ignoreOrder: true);
    }

    [Fact]
    public async Task AggregateAsync_ShouldRecordAnError_WhenASourceThrows()
    {
        var broken = Substitute.For<IDataSource>();
        broken.Name.Returns("broken");
        broken.FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>()).Returns<SourceResult>(_ => throw new InvalidOperationException("bug"));

        await Should.ThrowAsync<InvalidOperationException>(() => Service(broken).AggregateAsync(DefaultQuery, TestContext.Current.CancellationToken));

        _durations.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["outcome"].ShouldBe("error");
    }

    [Fact]
    public async Task AggregateAsync_ShouldRecordACancellation_WhenTheCallerGivesUp()
    {
        using var cts = new CancellationTokenSource();
        var pending = Service(TestSources.Hanging("slow")).AggregateAsync(DefaultQuery, cts.Token);

        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => pending);
        _durations.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["outcome"].ShouldBe("cancelled");
    }

    [Fact]
    public async Task StreamAsync_ShouldRecordItsDurationInStreamMode_WhenEverySourceHasBeenYielded()
    {
        var service = Service(Source("news", new SourceSuccess([])), Source("weather", new SourceFailure("down")));

        await ToListAsync(service.StreamAsync(DefaultQuery, TestContext.Current.CancellationToken));

        _durations.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags.ShouldBe(
            new Dictionary<string, object?> { ["mode"] = "stream", ["outcome"] = "degraded" }, ignoreOrder: true);
    }

    [Fact]
    public async Task StreamAsync_ShouldRecordACancellation_WhenTheConsumerStopsEarly()
    {
        var service = Service(TestSources.Hanging("slow"), Source("fast", new SourceSuccess([])));

        await foreach (var _ in service.StreamAsync(DefaultQuery, TestContext.Current.CancellationToken))
        {
            break;
        }

        _durations.GetMeasurementSnapshot().ShouldHaveSingleItem().Tags["outcome"].ShouldBe("cancelled");
    }

    public void Dispose()
    {
        _durations.Dispose();
        _meters.Dispose();
    }

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

    private AggregationService Service(params IDataSource[] sources) => new(new DataSourceRegistry(sources), _telemetry);

    private static SourceResult Answer(string kind) => kind switch
    {
        "success" => new SourceSuccess([]),
        "cached" => new SourceCached([], TimeSpan.FromMinutes(1)),
        "fallback" => new SourceFallback([], TimeSpan.Zero, "secondary"),
        _ => new SourceFailure("down"),
    };

    /// <summary>A source that answers only by being cancelled.</summary>
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
