using System.Diagnostics;
using System.Text.Json.Serialization;
using Aggregator.Domain.Items;

namespace Aggregator.Infrastructure.Sources.Caching;

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
