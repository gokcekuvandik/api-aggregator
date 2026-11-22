using Aggregator.Domain.Items;

namespace Aggregator.Infrastructure.Sources.Caching;

internal sealed record NewsDetailsPayload(string? Author, int? Points, string? Publisher) : DetailsPayload
{
    public override ItemDetails ToDetails() => new NewsDetails(Author, Points, Publisher);
}
