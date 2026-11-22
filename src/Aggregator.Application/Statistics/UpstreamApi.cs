namespace Aggregator.Application.Statistics;

/// <summary>One external API: the source it serves, and the secondary provider's name, or null for the source's primary provider.</summary>
public readonly record struct UpstreamApi(string Source, string? Provider);
