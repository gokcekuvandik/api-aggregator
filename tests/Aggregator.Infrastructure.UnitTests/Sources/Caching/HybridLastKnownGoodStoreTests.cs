using Aggregator.Application.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregator.Infrastructure.UnitTests.Sources.Caching;

public sealed class HybridLastKnownGoodStoreTests : IDisposable
{
    private const string Key = "source:weather:v1:city=athens";
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private readonly ServiceProvider _provider = CachingHost.Build();

    private ILastKnownGoodStore Store => _provider.GetRequiredService<ILastKnownGoodStore>();

    [Fact]
    public async Task FindAsync_ShouldReturnNullAndStoreNothing_WhenKeyWasNeverSaved()
    {
        (await Store.FindAsync(Key, TestContext.Current.CancellationToken)).ShouldBeNull();
        (await Store.FindAsync(Key, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task FindAsync_ShouldReturnTheSavedItems_WhenKeyWasSaved()
    {
        var entry = new LastKnownGood(CachingHost.Entry().Items, CachingHost.StoredAt);
        await Store.FindAsync(Key, TestContext.Current.CancellationToken);

        await Store.SaveAsync(Key, entry, Lifetime, TestContext.Current.CancellationToken);

        var found = (await Store.FindAsync(Key, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        found.StoredAt.ShouldBe(entry.StoredAt);
        found.Items.ShouldBe(entry.Items);
    }

    [Fact]
    public async Task SaveAsync_ShouldReplaceTheEntry_WhenKeyIsSavedAgain()
    {
        await Store.SaveAsync(Key, new LastKnownGood([], CachingHost.StoredAt), Lifetime, TestContext.Current.CancellationToken);

        await Store.SaveAsync(Key, new LastKnownGood([], CachingHost.StoredAt.AddMinutes(1)), Lifetime, TestContext.Current.CancellationToken);

        (await Store.FindAsync(Key, TestContext.Current.CancellationToken)).ShouldNotBeNull().StoredAt.ShouldBe(CachingHost.StoredAt.AddMinutes(1));
    }

    [Fact]
    public async Task FindAsync_ShouldReturnNull_WhenTheLifetimeHasPassed()
    {
        await Store.SaveAsync(Key, new LastKnownGood([], CachingHost.StoredAt), TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(400), TestContext.Current.CancellationToken);

        (await Store.FindAsync(Key, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task SaveAsync_ShouldNotFillTheFreshCache_WhenTheSameKeyIsUsedThere()
    {
        await Store.SaveAsync(Key, new LastKnownGood([], CachingHost.StoredAt), Lifetime, TestContext.Current.CancellationToken);
        var fetched = false;

        await _provider.GetRequiredService<ISourceCache>().GetOrFetchAsync(Key, TimeSpan.FromMinutes(5), _ =>
        {
            fetched = true;
            return Task.FromResult<SourceResult>(new SourceSuccess([]));
        }, result => CachingHost.Entry(), TestContext.Current.CancellationToken);

        fetched.ShouldBeTrue();
    }

    public void Dispose() => _provider.Dispose();
}
