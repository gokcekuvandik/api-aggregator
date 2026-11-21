namespace Aggregator.Infrastructure.Sources;

/// <summary>The public source a request is for, and the named HTTP client that sends it; they differ for a secondary provider.</summary>
internal readonly record struct SourceClient(string SourceName, string ClientName);
