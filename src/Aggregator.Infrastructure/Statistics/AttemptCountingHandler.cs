namespace Aggregator.Infrastructure.Statistics;

/// <summary>Sits inside the resilience pipeline and counts each attempt on the counter that <see cref="StatisticsHandler"/> attached to the request.</summary>
internal sealed class AttemptCountingHandler : DelegatingHandler
{
    private static readonly HttpRequestOptionsKey<AttemptCounter> Key = new("Aggregator.Attempts");

    /// <summary>Attaches a fresh counter; a copy of the request made for a retry carries the same counter, since options are copied by reference.</summary>
    internal static AttemptCounter Attach(HttpRequestMessage request)
    {
        var counter = new AttemptCounter();
        request.Options.Set(Key, counter);
        return counter;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(Key, out var counter))
        {
            counter.Increment();
        }

        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Attempts made for one request; retries and hedged attempts may increment it from several threads.</summary>
internal sealed class AttemptCounter
{
    private int _count;

    public int Count => Volatile.Read(ref _count);

    public void Increment() => Interlocked.Increment(ref _count);
}
