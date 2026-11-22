namespace Aggregator.Domain.Items;

/// <summary>An article or story; which fields are set depends on the provider.</summary>
public sealed record NewsDetails(string? Author, int? Points, string? Publisher) : ItemDetails;
