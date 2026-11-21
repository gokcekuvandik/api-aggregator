using Aggregator.Application.Aggregation;
using Aggregator.Application.Aggregation.Sorting;
using Aggregator.Application.Sources;
using Aggregator.Application.UnitTests.TestData;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using NSubstitute;

namespace Aggregator.Application.UnitTests.Sources;

public sealed class CachedDataSourceTests : IDisposable
{
    private const string Name = "repositories";
    private const string Provider = "hacker-news";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly AggregationQuery Query = new(Page: 1, PageSize: 20) { Text = "dotnet" };

    private readonly StubClock _clock = new(Now);
    private readonly FakeSourceCache _cache;
    private readonly TestMeterFactory _meters = new();
    private readonly MetricCollector<long> _lookups;

    public CachedDataSourceTests()
    {
        _cache = new FakeSourceCache(_clock);
        _lookups = new MetricCollector<long>(_meters, SourceCacheMetrics.MeterName, SourceCacheMetrics.LookupsName);
    }

    public enum Inner
    {
        Success,
        SecondaryFallback,
        StoredFallback,
        Failure,
    }

    /// <summary>Decision table: what the inner source answers on a miss → what is returned, whether it is cached, and the lookup result recorded.</summary>
    public static TheoryData<Inner, Type, bool, string> MissTable => new()
    {
        { Inner.Success, typeof(SourceSuccess), true, "miss" },
        { Inner.SecondaryFallback, typeof(SourceFallback), true, "miss" },
        { Inner.StoredFallback, typeof(SourceFallback), false, "stale" },
        { Inner.Failure, typeof(SourceFailure), false, "miss" },
    };

    [Theory]
    [MemberData(nameof(MissTable))]
    public async Task FetchAsync_ShouldReturnInnerResultAndCacheOnlyLiveAnswers_WhenEntryIsMissing(Inner inner, Type expected, bool cached, string lookup)
    {
        var result = Result(inner);
        var source = Cached(TestSources.Returning(Name, result, SearchFields.Text));

        var fetched = await source.FetchAsync(Query, TestContext.Current.CancellationToken);

        fetched.ShouldBeSameAs(result);
        fetched.ShouldBeOfType(expected);
        _cache.Stored.Count.ShouldBe(cached ? 1 : 0);
        LookupResults().ShouldBe([lookup]);
    }

    [Theory]
    [InlineData(Inner.Success, null)]
    [InlineData(Inner.SecondaryFallback, Provider)]
    public async Task FetchAsync_ShouldServeCachedItemsWithAgeAndProvider_WhenEntryIsFresh(Inner inner, string? provider)
    {
        var upstream = TestSources.Returning(Name, Result(inner), SearchFields.Text);
        var source = Cached(upstream);
        await source.FetchAsync(Query, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(4));

        var result = await source.FetchAsync(Query, TestContext.Current.CancellationToken);

        var cached = result.ShouldBeOfType<SourceCached>();
        cached.Items.Select(i => i.Id).ShouldBe(["live-1"]);
        cached.Age.ShouldBe(TimeSpan.FromMinutes(4));
        cached.Provider.ShouldBe(provider);
        await upstream.Received(1).FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>());
        LookupResults().ShouldBe(["miss", "hit"]);
    }

    [Fact]
    public async Task FetchAsync_ShouldHitTheSameEntry_WhenOnlySortCategoryOrPageDiffer()
    {
        var upstream = TestSources.Returning(Name, Result(Inner.Success), SearchFields.Text);
        var source = Cached(upstream);
        await source.FetchAsync(Query, TestContext.Current.CancellationToken);

        var result = await source.FetchAsync(
            Query with { Page = 2, PageSize = 5, SortBy = ItemSortField.Date, SortOrder = Application.Aggregation.Sorting.SortDirection.Ascending, Category = Name },
            TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceCached>();
        await upstream.Received(1).FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchAsync_ShouldHitTheSameEntry_WhenAFieldTheSourceIgnoresDiffers()
    {
        var upstream = TestSources.Returning(Name, Result(Inner.Success), SearchFields.Text);
        var source = Cached(upstream);
        await source.FetchAsync(Query, TestContext.Current.CancellationToken);

        var result = await source.FetchAsync(Query with { City = "Athens" }, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceCached>();
    }

    [Fact]
    public async Task FetchAsync_ShouldFetchAgain_WhenAUsedFieldDiffers()
    {
        var upstream = TestSources.Returning(Name, Result(Inner.Success), SearchFields.Text);
        var source = Cached(upstream);
        await source.FetchAsync(Query, TestContext.Current.CancellationToken);

        var result = await source.FetchAsync(Query with { Text = "rust" }, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceSuccess>();
        _cache.Stored.Select(s => s.Key).ShouldBe(["source:repositories:v1:q=dotnet", "source:repositories:v1:q=rust"]);
    }

    [Fact]
    public async Task FetchAsync_ShouldFetchAgain_WhenTheTimeToLiveHasPassed()
    {
        var upstream = TestSources.Returning(Name, Result(Inner.Success), SearchFields.Text);
        var source = Cached(upstream);
        await source.FetchAsync(Query, TestContext.Current.CancellationToken);
        _clock.Advance(Ttl);

        var result = await source.FetchAsync(Query, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceSuccess>();
        _cache.Stored.ShouldAllBe(stored => stored.Ttl == Ttl);
        await upstream.Received(2).FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchAsync_ShouldReportCached_WhenAnotherCallersFetchAnswered()
    {
        _cache.AnswerForAnotherCaller = new CachedItems(TestItems.Many(1, "joined"), Now);
        var upstream = TestSources.Returning(Name, Result(Inner.Success), SearchFields.Text);

        var result = await Cached(upstream).FetchAsync(Query, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceCached>().Age.ShouldBe(TimeSpan.Zero);
        await upstream.DidNotReceive().FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchAsync_ShouldReportZeroAge_WhenTheEntryIsStampedAfterNow()
    {
        _cache.AnswerForAnotherCaller = new CachedItems(TestItems.Many(1, "joined"), Now.AddSeconds(5));

        var result = await Cached(TestSources.Returning(Name, Result(Inner.Success))).FetchAsync(Query, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceCached>().Age.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public async Task FetchAsync_ShouldPropagateAndCacheNothing_WhenInnerSourceHasABug()
    {
        var upstream = TestSources.Returning(Name, Result(Inner.Success));
        upstream.FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>()).Returns<SourceResult>(_ => throw new InvalidOperationException("bug"));

        await Should.ThrowAsync<InvalidOperationException>(() => Cached(upstream).FetchAsync(Query, TestContext.Current.CancellationToken));

        _cache.Stored.ShouldBeEmpty();
    }

    [Fact]
    public async Task FetchAsync_ShouldThrow_WhenCallerCancels()
    {
        var upstream = TestSources.Returning(Name, Result(Inner.Success));
        upstream.FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>())
            .Returns<SourceResult>(call => throw new OperationCanceledException(call.Arg<CancellationToken>()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => Cached(upstream).FetchAsync(Query, cts.Token));
    }

    [Fact]
    public void Members_ShouldComeFromTheInnerSource_WhenWrapped()
    {
        var upstream = TestSources.Returning(Name, Result(Inner.Success), SearchFields.Text);

        var source = Cached(upstream);

        source.Inner.ShouldBeSameAs(upstream);
        source.Name.ShouldBe(Name);
        source.Categories.ShouldBe([Name]);
        source.UsedFields.ShouldBe(SearchFields.Text);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_ShouldThrow_WhenTheTimeToLiveIsNotPositive(int ttlSeconds) =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new CachedDataSource(TestSources.Returning(Name, Result(Inner.Success)), _cache, TimeSpan.FromSeconds(ttlSeconds), _clock, new SourceCacheMetrics(_meters)));

    public void Dispose()
    {
        _lookups.Dispose();
        _meters.Dispose();
    }

    private CachedDataSource Cached(IDataSource inner) => new(inner, _cache, Ttl, _clock, new SourceCacheMetrics(_meters));

    private List<string?> LookupResults() =>
        [.. _lookups.GetMeasurementSnapshot().Select(m =>
        {
            m.Tags["source"].ShouldBe(Name);
            return m.Tags["result"] as string;
        })];

    private static SourceResult Result(Inner inner) => inner switch
    {
        Inner.Success => new SourceSuccess(TestItems.Many(1, "live")),
        Inner.SecondaryFallback => new SourceFallback(TestItems.Many(1, "live"), TimeSpan.Zero, Provider),
        Inner.StoredFallback => new SourceFallback(TestItems.Many(1, "stored"), TimeSpan.FromMinutes(30)),
        _ => new SourceFailure("repositories answered HTTP 500."),
    };
}
