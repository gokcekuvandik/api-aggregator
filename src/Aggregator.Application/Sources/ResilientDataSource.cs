using Aggregator.Application.Aggregation;
using Aggregator.Application.Telemetry;

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
/// Live answers are stored for later fallbacks and kept for the last-known-good lifetime. Caller cancellation and provider bugs still throw.
/// Each time the primary fails, a fallback activation is counted with what stood in for it.
/// </summary>
public sealed class ResilientDataSource : IDataSource
{
    private readonly ILastKnownGoodStore _store;
    private readonly TimeProvider _time;
    private readonly TimeSpan _lastKnownGoodLifetime;
    private readonly AggregationTelemetry _telemetry;

    public ResilientDataSource(
        SourceProvider primary,
        SourceProvider? secondary,
        ILastKnownGoodStore store,
        TimeProvider time,
        TimeSpan lastKnownGoodLifetime,
        AggregationTelemetry telemetry)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lastKnownGoodLifetime, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(telemetry);
        if (secondary is not null && !secondary.Source.Categories.SequenceEqual(primary.Source.Categories, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The secondary provider '{secondary.Name}' must serve the same categories as '{primary.Name}'.", nameof(secondary));
        }

        Primary = primary;
        Secondary = secondary;
        _store = store;
        _time = time;
        _lastKnownGoodLifetime = lastKnownGoodLifetime;
        _telemetry = telemetry;
    }

    public SourceProvider Primary { get; }

    public SourceProvider? Secondary { get; }

    public string Name => Primary.Source.Name;

    public IReadOnlyList<string> Categories => Primary.Source.Categories;

    public SearchFields UsedFields => Primary.Source.UsedFields | (Secondary?.Source.UsedFields ?? SearchFields.None);

    public async Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var key = SourceCacheKey.For(Name, UsedFields, query);

        var primary = await FetchWithinTimeoutAsync(Primary, query, cancellationToken);
        if (primary is not SourceFailure primaryFailure)
        {
            await StoreAsync(key, primary, cancellationToken);
            return primary;
        }

        if (Secondary is not null)
        {
            var secondary = await FetchWithinTimeoutAsync(Secondary, query, cancellationToken);
            if (secondary is not SourceFailure)
            {
                await StoreAsync(key, secondary, cancellationToken);
                _telemetry.SecondaryStoodIn(Name, Secondary.Name);
                return new SourceFallback(secondary.Items, TimeSpan.Zero, Secondary.Name);
            }
        }

        if (await _store.FindAsync(key, cancellationToken) is { } stored)
        {
            _telemetry.LastKnownGoodStoodIn(Name);
            return new SourceFallback(stored.Items, _time.GetUtcNow() - stored.StoredAt);
        }

        _telemetry.NothingStoodIn(Name);
        return primaryFailure;
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

    private ValueTask StoreAsync(string key, SourceResult result, CancellationToken cancellationToken) =>
        _store.SaveAsync(key, new LastKnownGood(result.Items, _time.GetUtcNow()), _lastKnownGoodLifetime, cancellationToken);
}
