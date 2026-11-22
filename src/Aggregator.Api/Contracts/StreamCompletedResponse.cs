namespace Aggregator.Api.Contracts;

/// <summary>Data of the final <c>done</c> event; <c>hasData</c> is false when every source failed.</summary>
public sealed record StreamCompletedResponse(int SourceCount, bool HasData);
