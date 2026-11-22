using Aggregator.Domain.Items;

namespace Aggregator.Infrastructure.Sources.Caching;

internal sealed record RepositoryDetailsPayload(int Stars, int Forks, string? Language) : DetailsPayload
{
    public override ItemDetails ToDetails() => new RepositoryDetails(Stars, Forks, Language);
}
