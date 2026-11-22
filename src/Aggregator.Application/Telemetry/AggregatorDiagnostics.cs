using System.Diagnostics;

namespace Aggregator.Application.Telemetry;

/// <summary>The name shared by the Aggregator's own meter and activity source; the service defaults listen to both.</summary>
public static class AggregatorDiagnostics
{
    public const string Name = "Aggregator";

    /// <summary>The span attribute naming the source.</summary>
    public const string SourceTag = "aggregator.source";

    /// <summary>The span attribute naming the provider.</summary>
    public const string ProviderTag = "aggregator.provider";

    internal static ActivitySource ActivitySource { get; } = new(Name);
}
