namespace Aggregator.Infrastructure.Statistics;

/// <summary>Attempts made for one request; retries and hedged attempts may increment it from several threads.</summary>
internal sealed class AttemptCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Increment() => Interlocked.Increment(ref _count);
}
