using Aggregator.Application.Statistics;

namespace Aggregator.Infrastructure.Sources.Resilience;

/// <summary>An external API whose circuit breaker state is reported, registered once per resilient client.</summary>
internal sealed record TrackedCircuit(UpstreamApi Api);
