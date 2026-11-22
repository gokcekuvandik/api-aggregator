using System.Text.Json.Serialization;

namespace Aggregator.Infrastructure.Sources.News.HackerNews;

internal sealed record HackerNewsHit(
    [property: JsonPropertyName("objectID")] string ObjectId,
    DateTimeOffset CreatedAt,
    string? Title = null,
    string? Url = null,
    string? Author = null,
    int? Points = null);
