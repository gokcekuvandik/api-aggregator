namespace Aggregator.Domain.Items;

/// <summary>Fields only one kind of source has; only the records below derive from it.</summary>
public abstract record ItemDetails
{
    private protected ItemDetails()
    {
    }
}

/// <summary>A forecast reading; temperatures are in °C and may be missing.</summary>
public sealed record WeatherDetails(string Location, double? MinTemperature, double? MaxTemperature, string Conditions) : ItemDetails;

/// <summary>An article or story; which fields are set depends on the provider.</summary>
public sealed record NewsDetails(string? Author, int? Points, string? Publisher) : ItemDetails;

/// <summary>A code repository.</summary>
public sealed record RepositoryDetails(int Stars, int Forks, string? Language) : ItemDetails;
