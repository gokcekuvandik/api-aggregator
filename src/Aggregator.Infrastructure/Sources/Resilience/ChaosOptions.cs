namespace Aggregator.Infrastructure.Sources.Resilience;

/// <summary>Fault and latency injection for one provider's client, bound from <c>{section}:Chaos</c>; off unless <see cref="Enabled"/>.</summary>
internal sealed class ChaosOptions
{
    public bool Enabled { get; set; }

    /// <summary>Share of attempts, 0–1, that fail with a transport error before reaching the upstream.</summary>
    public double FaultRate { get; set; }

    /// <summary>Share of attempts, 0–1, delayed by <see cref="Latency"/>.</summary>
    public double LatencyRate { get; set; }

    public TimeSpan Latency { get; set; } = TimeSpan.FromSeconds(2);
}
