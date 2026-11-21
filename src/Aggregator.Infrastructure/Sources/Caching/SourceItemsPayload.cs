using System.Diagnostics;
using System.Text.Json.Serialization;
using Aggregator.Application.Sources;
using Aggregator.Domain.Items;

namespace Aggregator.Infrastructure.Sources.Caching;

/// <summary>
/// The serialized form of cached source items, so domain types stay free of serializer attributes.
/// The same shape is written to the in-memory cache and to a distributed one, if one is added.
/// </summary>
internal sealed record SourceItemsPayload(DateTimeOffset StoredAt, string? Provider, IReadOnlyList<ItemPayload> Items)
{
    public static SourceItemsPayload From(CachedItems entry) =>
        new(entry.StoredAt, entry.Provider, [.. entry.Items.Select(ItemPayload.From)]);

    public IReadOnlyList<AggregatedItem> ToItems() => [.. Items.Select(item => item.ToItem())];

    public CachedItems ToCachedItems() => new(ToItems(), StoredAt, Provider);
}

internal sealed record ItemPayload(
    string Id,
    string Source,
    string Category,
    string Title,
    string? Summary,
    Uri? Url,
    DateTimeOffset PublishedAt,
    double Relevance,
    DetailsPayload Details)
{
    public static ItemPayload From(AggregatedItem item) => new(
        item.Id, item.Source, item.Category, item.Title, item.Summary, item.Url, item.PublishedAt, item.Relevance.Value, DetailsPayload.From(item.Details));

    public AggregatedItem ToItem() => new(Id, Source, Category, Title, Summary, Url, PublishedAt, RelevanceScore.From(Relevance), Details.ToDetails());
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(WeatherDetailsPayload), "weather")]
[JsonDerivedType(typeof(NewsDetailsPayload), "news")]
[JsonDerivedType(typeof(RepositoryDetailsPayload), "repository")]
internal abstract record DetailsPayload
{
    public static DetailsPayload From(ItemDetails details) => details switch
    {
        WeatherDetails weather => new WeatherDetailsPayload(weather.Location, weather.MinTemperature, weather.MaxTemperature, weather.Conditions),
        NewsDetails news => new NewsDetailsPayload(news.Author, news.Points, news.Publisher),
        RepositoryDetails repository => new RepositoryDetailsPayload(repository.Stars, repository.Forks, repository.Language),
        _ => throw new UnreachableException($"Unknown item details {details.GetType().Name}."),
    };

    public abstract ItemDetails ToDetails();
}

internal sealed record WeatherDetailsPayload(string Location, double? MinTemperature, double? MaxTemperature, string Conditions) : DetailsPayload
{
    public override ItemDetails ToDetails() => new WeatherDetails(Location, MinTemperature, MaxTemperature, Conditions);
}

internal sealed record NewsDetailsPayload(string? Author, int? Points, string? Publisher) : DetailsPayload
{
    public override ItemDetails ToDetails() => new NewsDetails(Author, Points, Publisher);
}

internal sealed record RepositoryDetailsPayload(int Stars, int Forks, string? Language) : DetailsPayload
{
    public override ItemDetails ToDetails() => new RepositoryDetails(Stars, Forks, Language);
}

[JsonSerializable(typeof(SourceItemsPayload))]
internal sealed partial class SourceCacheJsonContext : JsonSerializerContext;
