using Aggregator.Application.Sources;
using Aggregator.Domain.Items;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregator.Infrastructure.UnitTests.Sources.Caching;

public sealed class HybridSourceCacheTests : IDisposable
{
    private const string Key = "source:repositories:v1:q=dotnet";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly SourceResult Live = new SourceSuccess([]);
    private static readonly SourceResult Failure = new SourceFailure("repositories answered HTTP 500.");

    private readonly ServiceProvider _provider = CachingHost.Build();
    private int _fetches;

    private ISourceCache Cache => _provider.GetRequiredService<ISourceCache>();

    [Fact]
    public async Task GetOrFetchAsync_ShouldReturnTheFetchedResult_WhenTheKeyIsMissing()
    {
        var lookup = await LookupAsync(Key, Ttl, CachingHost.Entry());

        lookup.ShouldBeOfType<SourceCacheLookup.Fetched>().Result.ShouldBeSameAs(Live);
        _fetches.ShouldBe(1);
    }

    [Fact]
    public async Task GetOrFetchAsync_ShouldReturnTheStoredCopy_WhenCalledAgainWithinTheTimeToLive()
    {
        var entry = CachingHost.Entry(provider: "hacker-news");
        await LookupAsync(Key, Ttl, entry);

        var second = (await LookupAsync(Key, Ttl, entry)).ShouldBeOfType<SourceCacheLookup.Hit>().Entry;

        _fetches.ShouldBe(1);
        second.ShouldNotBeSameAs(entry);
        second.StoredAt.ShouldBe(entry.StoredAt);
        second.Provider.ShouldBe("hacker-news");
        second.Items.ShouldBe(entry.Items);
    }

    [Fact]
    public async Task GetOrFetchAsync_ShouldRunTheFetchOnceAndReportTheJoinersAsHits_WhenManyCallersMissTheSameKeyConcurrently()
    {
        var gate = new TaskCompletionSource();
        var callers = Enumerable.Range(0, 10)
            .Select(_ => Cache.GetOrFetchAsync(Key, Ttl, GatedFetch(gate, Live), _ => CachingHost.Entry(), TestContext.Current.CancellationToken).AsTask())
            .ToList();

        gate.SetResult();
        var lookups = await Task.WhenAll(callers);

        _fetches.ShouldBe(1);
        lookups.OfType<SourceCacheLookup.Fetched>().ShouldHaveSingleItem();
        lookups.OfType<SourceCacheLookup.Hit>().Count().ShouldBe(9);
    }

    [Fact]
    public async Task GetOrFetchAsync_ShouldGiveEveryJoinedCallerTheResultAndStoreNothing_WhenTheResultIsNotStored()
    {
        var gate = new TaskCompletionSource();
        var callers = Enumerable.Range(0, 5)
            .Select(_ => Cache.GetOrFetchAsync(Key, Ttl, GatedFetch(gate, Failure), _ => null, TestContext.Current.CancellationToken).AsTask())
            .ToList();

        gate.SetResult();
        var lookups = await Task.WhenAll(callers);
        await LookupAsync(Key, Ttl, CachingHost.Entry());

        lookups.ShouldAllBe(lookup => ((SourceCacheLookup.Fetched)lookup).Result == Failure);
        _fetches.ShouldBe(2);
    }

    [Fact]
    public async Task GetOrFetchAsync_ShouldGiveEveryWaitingCallerTheSameExceptionAndStoreNothing_WhenTheSharedFetchThrows()
    {
        var gate = new TaskCompletionSource();
        var failure = new InvalidOperationException("bug");
        var callers = Enumerable.Range(0, 5)
            .Select(_ => Cache.GetOrFetchAsync(Key, Ttl, async token =>
            {
                Interlocked.Increment(ref _fetches);
                await gate.Task.WaitAsync(token);
                throw failure;
            }, _ => CachingHost.Entry(), TestContext.Current.CancellationToken).AsTask())
            .ToList();

        gate.SetResult();
        foreach (var caller in callers)
        {
            (await Should.ThrowAsync<InvalidOperationException>(caller)).ShouldBeSameAs(failure);
        }

        await LookupAsync(Key, Ttl, CachingHost.Entry());
        _fetches.ShouldBe(2);
    }

    [Fact]
    public async Task GetOrFetchAsync_ShouldFetchAgain_WhenTheTimeToLiveHasPassed()
    {
        var shortTtl = TimeSpan.FromMilliseconds(100);
        await LookupAsync(Key, shortTtl, CachingHost.Entry());
        await Task.Delay(TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken);

        (await LookupAsync(Key, shortTtl, CachingHost.Entry())).ShouldBeOfType<SourceCacheLookup.Fetched>();

        _fetches.ShouldBe(2);
    }

    [Fact]
    public async Task GetOrFetchAsync_ShouldKeepEntriesApart_WhenKeysDiffer()
    {
        await LookupAsync(Key, Ttl, CachingHost.Entry());

        await LookupAsync(Key + "x", Ttl, CachingHost.Entry());

        _fetches.ShouldBe(2);
    }

    [Fact]
    public async Task GetOrFetchAsync_ShouldCacheEveryItemKind_WhenTheEntryIsStored()
    {
        IReadOnlyList<AggregatedItem> items =
        [
            CachingHost.Item("w", new WeatherDetails("Athens", MinTemperature: null, MaxTemperature: 21.5, "Clear sky")),
            CachingHost.Item("n", new NewsDetails(Author: "Ada", Points: 42, Publisher: null)),
            CachingHost.Item("r", new RepositoryDetails(Stars: 10, Forks: 2, Language: null)),
        ];
        await LookupAsync(Key, Ttl, new CachedItems(items, CachingHost.StoredAt));

        var cached = await LookupAsync(Key, Ttl, CachingHost.Entry());

        cached.ShouldBeOfType<SourceCacheLookup.Hit>().Entry.Items.ShouldBe(items);
    }

    public void Dispose() => _provider.Dispose();

    private ValueTask<SourceCacheLookup> LookupAsync(string key, TimeSpan ttl, CachedItems entry) =>
        Cache.GetOrFetchAsync(key, ttl, _ =>
        {
            Interlocked.Increment(ref _fetches);
            return Task.FromResult(Live);
        }, _ => entry, TestContext.Current.CancellationToken);

    private Func<CancellationToken, Task<SourceResult>> GatedFetch(TaskCompletionSource gate, SourceResult result) => async token =>
    {
        Interlocked.Increment(ref _fetches);
        await gate.Task.WaitAsync(token);
        return result;
    };
}
