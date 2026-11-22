namespace Aggregator.Domain.Statistics;

/// <summary>What <see cref="AnomalyRule"/> concludes about one external API's recent window.</summary>
public enum AnomalyVerdict
{
    /// <summary>The window holds too few requests to judge, so the previous state should stand.</summary>
    InsufficientSamples,

    Normal,

    Anomalous,
}
