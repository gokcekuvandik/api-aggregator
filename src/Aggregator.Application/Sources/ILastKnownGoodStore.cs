namespace Aggregator.Application.Sources;

/// <summary>Last-known-good items per source and search, read only when every provider of a source has failed.</summary>
public interface ILastKnownGoodStore
{
    ValueTask<LastKnownGood?> FindAsync(string key, CancellationToken cancellationToken);

    /// <summary>Stores <paramref name="entry"/> for <paramref name="lifetime"/>, after which it can no longer stand in for the source.</summary>
    ValueTask SaveAsync(string key, LastKnownGood entry, TimeSpan lifetime, CancellationToken cancellationToken);
}
