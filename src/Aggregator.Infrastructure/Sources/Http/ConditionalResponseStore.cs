using System.Diagnostics.CodeAnalysis;

namespace Aggregator.Infrastructure.Sources.Http;

/// <summary>Stored responses by absolute URL, bounded by evicting the least recently used URL.</summary>
internal sealed class ConditionalResponseStore(int capacity = ConditionalResponseStore.DefaultCapacity)
{
    public const int DefaultCapacity = 256;

    private readonly LruStore<StoredResponse> _entries = new(capacity);

    public bool TryGet(string key, [NotNullWhen(true)] out StoredResponse? response) => _entries.TryGet(key, out response);

    public void Set(string key, StoredResponse response) => _entries.Set(key, response);
}
