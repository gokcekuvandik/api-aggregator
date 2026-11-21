using Aggregator.Application.Sources;

namespace Aggregator.Infrastructure.Sources.Resilience;

/// <summary>Last-known-good items in process memory, bounded by evicting the least recently used search.</summary>
internal sealed class InMemoryLastKnownGoodStore(int capacity = InMemoryLastKnownGoodStore.DefaultCapacity) : ILastKnownGoodStore
{
    public const int DefaultCapacity = 256;

    private readonly LruStore<LastKnownGood> _entries = new(capacity);

    public LastKnownGood? Find(string key) => _entries.TryGet(key, out var entry) ? entry : null;

    public void Save(string key, LastKnownGood entry) => _entries.Set(key, entry);
}
