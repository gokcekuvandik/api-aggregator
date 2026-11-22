namespace Aggregator.Application.Statistics;

/// <summary>The single recorder for upstream requests and cache hits; every statistics consumer reads what it records.</summary>
public interface ISourceTelemetry
{
    /// <summary>Records one request as the caller saw it, including every retry, and how many attempts it took.</summary>
    void RecordRequest(UpstreamApi api, TimeSpan duration, RequestOutcome outcome, int attempts);

    /// <summary>Records a source answer served from the cache, which never reached the upstream.</summary>
    void RecordCacheHit(UpstreamApi api);
}

/// <summary>One external API: the source it serves, and the secondary provider's name, or null for the source's primary provider.</summary>
public readonly record struct UpstreamApi(string Source, string? Provider);

public enum RequestOutcome
{
    /// <summary>The upstream answered with a success or not-modified status.</summary>
    Success,

    /// <summary>The upstream answered with an error status, or the request failed or timed out after at least one attempt.</summary>
    Failure,

    /// <summary>The caller gave up before the upstream answered; the duration says nothing about the upstream.</summary>
    Cancelled,

    /// <summary>No attempt was sent, for example because the circuit was open; the duration says nothing about the upstream.</summary>
    Rejected,
}
