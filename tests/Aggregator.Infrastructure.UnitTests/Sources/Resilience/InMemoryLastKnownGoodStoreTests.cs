using Aggregator.Application.Sources;
using Aggregator.Infrastructure.Sources.Resilience;

namespace Aggregator.Infrastructure.UnitTests.Sources.Resilience;

public sealed class InMemoryLastKnownGoodStoreTests
{
    private static readonly DateTimeOffset StoredAt = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Find_ShouldReturnNull_WhenKeyWasNeverSaved() =>
        new InMemoryLastKnownGoodStore().Find("source:weather:v1:").ShouldBeNull();

    [Fact]
    public void Find_ShouldReturnSavedEntry_WhenKeyWasSaved()
    {
        var store = new InMemoryLastKnownGoodStore();
        var entry = new LastKnownGood([], StoredAt);

        store.Save("k", entry);

        store.Find("k").ShouldBeSameAs(entry);
    }

    [Fact]
    public void Save_ShouldReplaceEntry_WhenKeyIsSavedAgain()
    {
        var store = new InMemoryLastKnownGoodStore();
        store.Save("k", new LastKnownGood([], StoredAt));

        store.Save("k", new LastKnownGood([], StoredAt.AddMinutes(1)));

        store.Find("k").ShouldNotBeNull().StoredAt.ShouldBe(StoredAt.AddMinutes(1));
    }

    [Fact]
    public void Save_ShouldEvictLeastRecentlyUsedKey_WhenCapacityIsReached()
    {
        var store = new InMemoryLastKnownGoodStore(capacity: 2);
        store.Save("a", new LastKnownGood([], StoredAt));
        store.Save("b", new LastKnownGood([], StoredAt));
        store.Find("a");

        store.Save("c", new LastKnownGood([], StoredAt));

        store.Find("b").ShouldBeNull();
        store.Find("a").ShouldNotBeNull();
        store.Find("c").ShouldNotBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_ShouldThrow_WhenCapacityIsNotPositive(int capacity) =>
        Should.Throw<ArgumentOutOfRangeException>(() => new InMemoryLastKnownGoodStore(capacity));
}
