using Aggregator.Application.Aggregation;

namespace Aggregator.Application.Sources;

/// <summary>One provider behind a source, with the most time a single fetch from it may take.</summary>
public sealed record SourceProvider
{
    public SourceProvider(string name, IDataSource source, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        Name = name;
        Source = source;
        Timeout = timeout;
    }

    public string Name { get; }

    public IDataSource Source { get; }

    public TimeSpan Timeout { get; }
}

/// <summary>
/// Tries the primary provider, then the secondary, then the last-known-good items, each fetch under its provider's timeout.
/// Live answers are stored for later fallbacks. Caller cancellation and provider bugs still throw.
/// </summary>
public sealed class ResilientDataSource : IDataSource
{
    private readonly ILastKnownGoodStore _store;
    private readonly TimeProvider _time;

    public ResilientDataSource(SourceProvider primary, SourceProvider? secondary, ILastKnownGoodStore store, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);
        if (secondary is not null && !secondary.Source.Categories.SequenceEqual(primary.Source.Categories, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The secondary provider '{secondary.Name}' must serve the same categories as '{primary.Name}'.", nameof(secondary));
        }

        Primary = primary;
        Secondary = secondary;
        _store = store;
        _time = time;
    }

    public SourceProvider Primary { get; }

    public SourceProvider? Secondary { get; }

    public string Name => Primary.Source.Name;

    public IReadOnlyList<string> Categories => Primary.Source.Categories;

    public async Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var key = LastKnownGoodKey.For(Name, query);

        var primary = await FetchWithinTimeoutAsync(Primary, query, cancellationToken);
        if (primary is not SourceFailure primaryFailure)
        {
            Store(key, primary);
            return primary;
        }

        if (Secondary is not null)
        {
            var secondary = await FetchWithinTimeoutAsync(Secondary, query, cancellationToken);
            if (secondary is not SourceFailure)
            {
                Store(key, secondary);
                return new SourceFallback(secondary.Items, TimeSpan.Zero, Secondary.Name);
            }
        }

        return _store.Find(key) is { } stored
            ? new SourceFallback(stored.Items, _time.GetUtcNow() - stored.StoredAt)
            : primaryFailure;
    }

    private async Task<SourceResult> FetchWithinTimeoutAsync(SourceProvider provider, AggregationQuery query, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(provider.Timeout);
        try
        {
            return await provider.Source.FetchAsync(query, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            return SourceFailure.TimedOut(Name);
        }
    }

    private void Store(string key, SourceResult result) => _store.Save(key, new LastKnownGood(result.Items, _time.GetUtcNow()));
}
