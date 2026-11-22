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
