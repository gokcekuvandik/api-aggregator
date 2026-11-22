namespace Aggregator.Infrastructure.Sources.Relevance;

/// <summary>GitHub's search score and the repository's star count.</summary>
internal sealed record RepositorySignal(double SearchScore, int Stars);
