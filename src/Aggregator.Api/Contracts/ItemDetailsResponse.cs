using System.Diagnostics;
using System.Text.Json.Serialization;
using Aggregator.Domain.Items;

namespace Aggregator.Api.Contracts;

/// <summary>Source-specific fields of an item; <c>$type</c> says which shape follows.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(WeatherDetailsResponse), "weather")]
[JsonDerivedType(typeof(NewsDetailsResponse), "news")]
[JsonDerivedType(typeof(RepositoryDetailsResponse), "repository")]
public abstract record ItemDetailsResponse
{
    public static ItemDetailsResponse From(ItemDetails details) => details switch
    {
        WeatherDetails weather => new WeatherDetailsResponse(weather.Location, weather.MinTemperature, weather.MaxTemperature, weather.Conditions),
        NewsDetails news => new NewsDetailsResponse(news.Author, news.Points, news.Publisher),
        RepositoryDetails repository => new RepositoryDetailsResponse(repository.Stars, repository.Forks, repository.Language),
        null => throw new ArgumentNullException(nameof(details)),
        _ => throw new UnreachableException($"Unknown item details {details.GetType().Name}."),
    };
}

/// <summary>Temperatures are in °C.</summary>
public sealed record WeatherDetailsResponse(string Location, double? MinTemperature, double? MaxTemperature, string Conditions) : ItemDetailsResponse;

public sealed record NewsDetailsResponse(string? Author, int? Points, string? Publisher) : ItemDetailsResponse;

public sealed record RepositoryDetailsResponse(int Stars, int Forks, string? Language) : ItemDetailsResponse;
