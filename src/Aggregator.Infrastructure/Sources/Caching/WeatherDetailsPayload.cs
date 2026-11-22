using Aggregator.Domain.Items;

namespace Aggregator.Infrastructure.Sources.Caching;

internal sealed record WeatherDetailsPayload(string Location, double? MinTemperature, double? MaxTemperature, string Conditions) : DetailsPayload
{
    public override ItemDetails ToDetails() => new WeatherDetails(Location, MinTemperature, MaxTemperature, Conditions);
}
