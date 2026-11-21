using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Aggregator.Application.UnitTests.TestData;
using NSubstitute;

namespace Aggregator.Application.UnitTests.Sources;

public sealed class ResilientDataSourceTests
{
    private const string Name = "weather";
    private const string SecondaryName = "open-meteo";
    private const string PrimaryError = "weather answered HTTP 500.";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan StoredAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LastKnownGoodLifetime = TimeSpan.FromHours(24);
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly AggregationQuery Query = new(Page: 1, PageSize: 20);

    private readonly InMemoryStore _store = new();
    private readonly StubClock _clock = new(Now);

    public enum Primary
    {
        Success,
        Failure,
        Timeout,
    }

    public enum Secondary
    {
        None,
        Success,
        Failure,
    }

    public enum Expected
    {
        Live,
        SecondaryFallback,
        StoredFallback,
        Failed,
    }

    /// <summary>Decision table: primary outcome × secondary provider × last-known-good entry → what the source reports.</summary>
    public static TheoryData<Primary, Secondary, bool, Expected> DecisionTable => new()
    {
        { Primary.Success, Secondary.None, false, Expected.Live },
        { Primary.Success, Secondary.None, true, Expected.Live },
        { Primary.Success, Secondary.Success, false, Expected.Live },
        { Primary.Success, Secondary.Success, true, Expected.Live },
        { Primary.Success, Secondary.Failure, false, Expected.Live },
        { Primary.Success, Secondary.Failure, true, Expected.Live },
        { Primary.Failure, Secondary.None, false, Expected.Failed },
        { Primary.Failure, Secondary.None, true, Expected.StoredFallback },
        { Primary.Failure, Secondary.Success, false, Expected.SecondaryFallback },
        { Primary.Failure, Secondary.Success, true, Expected.SecondaryFallback },
        { Primary.Failure, Secondary.Failure, false, Expected.Failed },
        { Primary.Failure, Secondary.Failure, true, Expected.StoredFallback },
        { Primary.Timeout, Secondary.None, false, Expected.Failed },
        { Primary.Timeout, Secondary.None, true, Expected.StoredFallback },
        { Primary.Timeout, Secondary.Success, false, Expected.SecondaryFallback },
        { Primary.Timeout, Secondary.Success, true, Expected.SecondaryFallback },
        { Primary.Timeout, Secondary.Failure, false, Expected.Failed },
        { Primary.Timeout, Secondary.Failure, true, Expected.StoredFallback },
    };

    [Theory]
    [MemberData(nameof(DecisionTable))]
    public async Task FetchAsync_ShouldFollowDecisionTable_WhenProvidersAndStoreVary(Primary primary, Secondary secondary, bool stored, Expected expected)
    {
        if (stored)
        {
            _store.Save(Key(Query), new LastKnownGood(TestItems.Many(1, "stored"), Now - StoredAge));
        }

        var source = Resilient(PrimaryProvider(primary), SecondaryProvider(secondary));

        var result = await source.FetchAsync(Query, TestContext.Current.CancellationToken);

        switch (expected)
        {
            case Expected.Live:
                result.ShouldBeOfType<SourceSuccess>().Items.Select(i => i.Id).ShouldBe(["primary-1"]);
                break;
            case Expected.SecondaryFallback:
                var fromSecondary = result.ShouldBeOfType<SourceFallback>();
                fromSecondary.Items.Select(i => i.Id).ShouldBe(["secondary-1"]);
                fromSecondary.Age.ShouldBe(TimeSpan.Zero);
                fromSecondary.Provider.ShouldBe(SecondaryName);
                break;
            case Expected.StoredFallback:
                var fromStore = result.ShouldBeOfType<SourceFallback>();
                fromStore.Items.Select(i => i.Id).ShouldBe(["stored-1"]);
                fromStore.Age.ShouldBe(StoredAge);
                fromStore.Provider.ShouldBeNull();
                break;
            default:
                result.ShouldBeOfType<SourceFailure>().Error.ShouldBe(primary == Primary.Timeout ? "weather timed out." : PrimaryError);
                break;
        }
    }

    [Fact]
    public async Task FetchAsync_ShouldStoreLiveItems_WhenPrimarySucceeds()
    {
        var source = Resilient(PrimaryProvider(Primary.Success));

        await source.FetchAsync(Query, TestContext.Current.CancellationToken);

        var stored = _store.Find(Key(Query)).ShouldNotBeNull();
        stored.Items.Select(i => i.Id).ShouldBe(["primary-1"]);
        stored.StoredAt.ShouldBe(Now);
        _store.Lifetimes.ShouldBe([LastKnownGoodLifetime]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_ShouldThrow_WhenLastKnownGoodLifetimeIsNotPositive(int seconds) =>
        Should.Throw<ArgumentOutOfRangeException>(() =>
            new ResilientDataSource(PrimaryProvider(Primary.Success), null, _store, _clock, TimeSpan.FromSeconds(seconds)));

    [Fact]
    public async Task FetchAsync_ShouldStoreSecondaryItems_WhenOnlySecondarySucceeds()
    {
        var source = Resilient(PrimaryProvider(Primary.Failure), SecondaryProvider(Secondary.Success));

        await source.FetchAsync(Query, TestContext.Current.CancellationToken);

        _store.Find(Key(Query)).ShouldNotBeNull().Items.Select(i => i.Id).ShouldBe(["secondary-1"]);
    }

    [Fact]
    public async Task FetchAsync_ShouldNotStoreAnything_WhenEveryProviderFails()
    {
        var source = Resilient(PrimaryProvider(Primary.Failure), SecondaryProvider(Secondary.Failure));

        await source.FetchAsync(Query, TestContext.Current.CancellationToken);

        _store.Count.ShouldBe(0);
    }

    [Fact]
    public async Task FetchAsync_ShouldServeEarlierLiveItemsWithTheirAge_WhenPrimaryLaterFails()
    {
        var primary = Substitute.For<IDataSource>();
        primary.Name.Returns(Name);
        primary.UsedFields.Returns(SearchFields.All);
        primary.FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SourceSuccess(TestItems.Many(1, "primary")), new SourceFailure(PrimaryError));
        var source = Resilient(new SourceProvider(Name, primary, Timeout));
        await source.FetchAsync(Query, TestContext.Current.CancellationToken);
        _clock.Advance(TimeSpan.FromMinutes(3));

        var result = await source.FetchAsync(Query, TestContext.Current.CancellationToken);

        var fallback = result.ShouldBeOfType<SourceFallback>();
        fallback.Items.Select(i => i.Id).ShouldBe(["primary-1"]);
        fallback.Age.ShouldBe(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public async Task FetchAsync_ShouldIgnoreStoredItemsOfAnotherSearch_WhenPrimaryFails()
    {
        _store.Save(Key(Query with { Text = "dotnet" }), new LastKnownGood(TestItems.Many(1, "stored"), Now));
        var source = Resilient(PrimaryProvider(Primary.Failure));

        var result = await source.FetchAsync(Query with { Text = "rust" }, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceFailure>();
    }

    [Fact]
    public async Task FetchAsync_ShouldThrowWithoutFallingBack_WhenCallerCancels()
    {
        _store.Save(Key(Query), new LastKnownGood(TestItems.Many(1, "stored"), Now));
        var secondary = SecondaryProvider(Secondary.Success)!;
        var source = Resilient(PrimaryProvider(Primary.Timeout), secondary);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => source.FetchAsync(Query, cts.Token));

        await secondary.Source.DidNotReceive().FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchAsync_ShouldPropagateException_WhenProviderHasABug()
    {
        var primary = Substitute.For<IDataSource>();
        primary.Name.Returns(Name);
        primary.UsedFields.Returns(SearchFields.All);
        primary.Categories.Returns([Name]);
        primary.FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>()).Returns<SourceResult>(_ => throw new InvalidOperationException("bug"));
        var source = Resilient(new SourceProvider(Name, primary, Timeout), SecondaryProvider(Secondary.Success));

        await Should.ThrowAsync<InvalidOperationException>(() => source.FetchAsync(Query, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FetchAsync_ShouldPassQueryAndALinkedToken_WhenCallingPrimary()
    {
        var primary = PrimaryProvider(Primary.Success);
        var source = Resilient(primary);
        using var cts = new CancellationTokenSource();

        await source.FetchAsync(Query, cts.Token);

        await primary.Source.Received(1).FetchAsync(Query, Arg.Is<CancellationToken>(token => token.CanBeCanceled));
    }

    [Fact]
    public void NameAndCategories_ShouldComeFromPrimary_WhenComposed()
    {
        var source = Resilient(PrimaryProvider(Primary.Success), SecondaryProvider(Secondary.Success));

        source.Name.ShouldBe(Name);
        source.Categories.ShouldBe([Name]);
        source.Primary.Name.ShouldBe(Name);
        source.Secondary.ShouldNotBeNull().Name.ShouldBe(SecondaryName);
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenSecondaryServesOtherCategories()
    {
        var other = Substitute.For<IDataSource>();
        other.Categories.Returns(["news"]);

        Should.Throw<ArgumentException>(() => Resilient(PrimaryProvider(Primary.Success), new SourceProvider(SecondaryName, other, Timeout)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SourceProvider_ShouldThrow_WhenTimeoutIsNotPositive(int milliseconds) =>
        Should.Throw<ArgumentOutOfRangeException>(() => new SourceProvider(Name, Substitute.For<IDataSource>(), TimeSpan.FromMilliseconds(milliseconds)));

    [Fact]
    public void UsedFields_ShouldCombinePrimaryAndSecondaryFields_WhenComposed()
    {
        var primary = TestSources.Returning(Name, new SourceSuccess([]), SearchFields.City);
        var secondary = TestSources.Returning(Name, new SourceSuccess([]), SearchFields.City | SearchFields.From);

        Resilient(new SourceProvider(Name, primary, Timeout), new SourceProvider(SecondaryName, secondary, Timeout))
            .UsedFields.ShouldBe(SearchFields.City | SearchFields.From);
        Resilient(new SourceProvider(Name, primary, Timeout)).UsedFields.ShouldBe(SearchFields.City);
    }

    private static string Key(AggregationQuery query) => SourceCacheKey.For(Name, SearchFields.All, query);

    private ResilientDataSource Resilient(SourceProvider primary, SourceProvider? secondary = null) =>
        new(primary, secondary, _store, _clock, LastKnownGoodLifetime);

    private static SourceProvider PrimaryProvider(Primary outcome) => new(Name, outcome switch
    {
        Primary.Success => Returning(Name, new SourceSuccess(TestItems.Many(1, "primary"))),
        Primary.Failure => Returning(Name, new SourceFailure(PrimaryError)),
        _ => Hanging(Name),
    }, outcome == Primary.Timeout ? ShortTimeout : Timeout);

    private static SourceProvider? SecondaryProvider(Secondary outcome) => outcome switch
    {
        Secondary.Success => new(SecondaryName, Returning(Name, new SourceSuccess(TestItems.Many(1, "secondary"))), Timeout),
        Secondary.Failure => new(SecondaryName, Returning(Name, new SourceFailure("weather could not be reached.")), Timeout),
        _ => null,
    };

    private static IDataSource Returning(string name, SourceResult result) => TestSources.Returning(name, result);

    private static IDataSource Hanging(string name)
    {
        var source = Substitute.For<IDataSource>();
        source.Name.Returns(name);
        source.Categories.Returns([name]);
        source.UsedFields.Returns(SearchFields.All);
        source.FetchAsync(Arg.Any<AggregationQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => WaitForCancellationAsync(call.Arg<CancellationToken>()));
        return source;
    }

    private static async Task<SourceResult> WaitForCancellationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("Unreachable: the delay only ends by cancellation.");
    }

    private sealed class InMemoryStore : ILastKnownGoodStore
    {
        private readonly Dictionary<string, LastKnownGood> _entries = new(StringComparer.Ordinal);

        public int Count => _entries.Count;

        public List<TimeSpan> Lifetimes { get; } = [];

        public LastKnownGood? Find(string key) => _entries.GetValueOrDefault(key);

        public void Save(string key, LastKnownGood entry) => _entries[key] = entry;

        public ValueTask<LastKnownGood?> FindAsync(string key, CancellationToken cancellationToken) => ValueTask.FromResult(Find(key));

        public ValueTask SaveAsync(string key, LastKnownGood entry, TimeSpan lifetime, CancellationToken cancellationToken)
        {
            Save(key, entry);
            Lifetimes.Add(lifetime);
            return ValueTask.CompletedTask;
        }
    }
}
