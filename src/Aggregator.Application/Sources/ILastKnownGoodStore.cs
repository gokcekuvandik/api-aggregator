using Aggregator.Domain.Items;

namespace Aggregator.Application.Sources;

/// <summary>The last items a source answered for one search, and when they were stored.</summary>
public sealed record LastKnownGood(IReadOnlyList<AggregatedItem> Items, DateTimeOffset StoredAt);

/// <summary>Last-known-good items per source and search, read only when every provider of a source has failed.</summary>
public interface ILastKnownGoodStore
{
    ValueTask<LastKnownGood?> FindAsync(string key, CancellationToken cancellationToken);

    /// <summary>Stores <paramref name="entry"/> for <paramref name="lifetime"/>, after which it can no longer stand in for the source.</summary>
    ValueTask SaveAsync(string key, LastKnownGood entry, TimeSpan lifetime, CancellationToken cancellationToken);
}
