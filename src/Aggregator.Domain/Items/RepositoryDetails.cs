namespace Aggregator.Domain.Items;

/// <summary>A code repository.</summary>
public sealed record RepositoryDetails(int Stars, int Forks, string? Language) : ItemDetails;
