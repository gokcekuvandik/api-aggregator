namespace Aggregator.Application.Statistics;

/// <summary>The single recorder for upstream requests and cache hits; every statistics consumer reads what it records.</summary>
public interface ISourceTelemetry
{
    /// <summary>Records one request as the caller saw it, including every retry, and how many attempts it took.</summary>
    void RecordRequest(UpstreamApi api, TimeSpan duration, RequestOutcome outcome, int attempts);

    /// <summary>Records a source answer served from the cache, which never reached the upstream.</summary>
    void RecordCacheHit(UpstreamApi api);
}
