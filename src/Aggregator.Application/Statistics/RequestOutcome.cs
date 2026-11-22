namespace Aggregator.Application.Statistics;

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
