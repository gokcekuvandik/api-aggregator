using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.UnitTests.TestDoubles;

/// <summary>
/// An in-memory second level that several service providers can share, standing in for Redis.
/// HybridCache ignores a registered <see cref="MemoryDistributedCache"/>, so this wraps one instead of being one.
/// </summary>
internal sealed class SharedDistributedCache : IDistributedCache
{
    private readonly MemoryDistributedCache inner = new(Options.Create(new MemoryDistributedCacheOptions()));

    public byte[]? Get(string key) => inner.Get(key);

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => inner.GetAsync(key, token);

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => inner.Set(key, value, options);

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
        inner.SetAsync(key, value, options, token);

    public void Refresh(string key) => inner.Refresh(key);

    public Task RefreshAsync(string key, CancellationToken token = default) => inner.RefreshAsync(key, token);

    public void Remove(string key) => inner.Remove(key);

    public Task RemoveAsync(string key, CancellationToken token = default) => inner.RemoveAsync(key, token);
}
