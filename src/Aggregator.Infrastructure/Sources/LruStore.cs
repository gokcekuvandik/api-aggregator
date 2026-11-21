using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Aggregator.Infrastructure.Sources;

/// <summary>
/// Values by string key, bounded by evicting the least recently used key. Safe for concurrent use.
/// The bound is soft: concurrent inserts of new keys can briefly exceed it by the number of writers.
/// Keys carry request text, so the bound must keep making room rather than freeze on the first keys seen.
/// </summary>
internal sealed class LruStore<TValue>
    where TValue : class
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _evictionLock = new();
    private readonly int _capacity;
    private long _clock;

    public LruStore(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    public bool TryGet(string key, [NotNullWhen(true)] out TValue? value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            entry.Touch(Tick());
            value = entry.Value;
            return true;
        }

        value = null;
        return false;
    }

    public void Set(string key, TValue value)
    {
        if (!_entries.ContainsKey(key))
        {
            EvictIfFull();
        }

        _entries[key] = new Entry(value, Tick());
    }

    private void EvictIfFull()
    {
        lock (_evictionLock)
        {
            while (_entries.Count >= _capacity && _entries.MinBy(pair => pair.Value.LastUsed) is { Key: { } oldest })
            {
                _entries.TryRemove(oldest, out _);
            }
        }
    }

    private long Tick() => Interlocked.Increment(ref _clock);

    private sealed class Entry(TValue value, long lastUsed)
    {
        private long _lastUsed = lastUsed;

        public TValue Value { get; } = value;

        public long LastUsed => Volatile.Read(ref _lastUsed);

        public void Touch(long now) => Volatile.Write(ref _lastUsed, now);
    }
}
