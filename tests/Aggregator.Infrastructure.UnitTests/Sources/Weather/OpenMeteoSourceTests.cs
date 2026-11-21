using Aggregator.Application.Sources;
using Aggregator.Domain.Items;
using Aggregator.Infrastructure.Sources.Relevance;
using Aggregator.Infrastructure.Sources.Weather;
using Aggregator.Infrastructure.Sources.Weather.OpenMeteo;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace Aggregator.Infrastructure.UnitTests.Sources.Weather;

public sealed class OpenMeteoSourceTests : IDisposable
{
    private readonly SourceTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task FetchAsync_ShouldRequestDailyForecast_WhenOptionsSetLocation()
    {
        _host.Http.Expect($"{StubHttpClientFactory.DefaultBaseAddress}v1/forecast")
            .WithExactQueryString("latitude=40.64&longitude=22.94&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=UTC&forecast_days=2")
            .Respond("application/json", SourceTestHost.Fixture("open-meteo-forecast.json"));

        await Source(new OpenMeteoOptions { Location = "Thessaloniki", Latitude = 40.64, Longitude = 22.94, ForecastDays = 2 })
            .FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        _host.Http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task FetchAsync_ShouldWriteCoordinatesWithoutExponent_WhenTheyAreTiny()
    {
        _host.Http.Expect("*").WithQueryString("latitude", "0.00001").WithQueryString("longitude", "-0.5").Respond("application/json", SourceTestHost.Fixture("open-meteo-forecast.json"));

        await Source(new OpenMeteoOptions { Latitude = 0.00001, Longitude = -0.5 }).FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        _host.Http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnOneItemPerDay_WhenForecastHasDays()
    {
        _host.Http.When("*").Respond("application/json", SourceTestHost.Fixture("open-meteo-forecast.json"));

        var result = await Source(new OpenMeteoOptions()).FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        var items = result.ShouldBeOfType<SourceSuccess>().Items;
        items.Select(i => i.Title).ShouldBe(["Athens: overcast, 10 to 18 °C", "Athens: rain, max 15 °C"]);
        items[1].PublishedAt.ShouldBe(new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnFailure_WhenDailyBlockIsMissing()
    {
        _host.Http.When("*").Respond("application/json", """{"latitude":1}""");

        var result = await Source(new OpenMeteoOptions()).FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceFailure>();
    }

    [Fact]
    public async Task FetchAsync_ShouldGeocodeCityThenForecast_WhenRequestHasCity()
    {
        _host.Http.Expect("https://geocoding-api.open-meteo.com/v1/search")
            .WithExactQueryString("name=Paris&count=1&language=en&format=json")
            .Respond("application/json", SourceTestHost.Fixture("open-meteo-geocoding.json"));
        _host.Http.Expect($"{StubHttpClientFactory.DefaultBaseAddress}v1/forecast")
            .WithQueryString("latitude", "48.85341")
            .WithQueryString("longitude", "2.3488")
            .Respond("application/json", SourceTestHost.Fixture("open-meteo-forecast.json"));

        var result = await Source(new OpenMeteoOptions()).FetchAsync(SourceTestHost.Query with { City = "Paris" }, TestContext.Current.CancellationToken);

        _host.Http.VerifyNoOutstandingExpectation();
        var items = result.ShouldBeOfType<SourceSuccess>().Items;
        items[0].Title.ShouldBe("Paris: overcast, 10 to 18 °C");
        items[0].Details.ShouldBe(new WeatherDetails("Paris", MinTemperature: 9.6, MaxTemperature: 18.4, Conditions: "overcast"));
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnNoItemsWithoutForecast_WhenCityIsNotFound()
    {
        _host.Http.When("https://geocoding-api.open-meteo.com/*").Respond("application/json", """{"generationtime_ms":0.1}""");
        var forecast = _host.Http.When($"{StubHttpClientFactory.DefaultBaseAddress}*").Respond("application/json", SourceTestHost.Fixture("open-meteo-forecast.json"));

        var result = await Source(new OpenMeteoOptions()).FetchAsync(SourceTestHost.Query with { City = "Nowhere" }, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceSuccess>().Items.ShouldBeEmpty();
        _host.Http.GetMatchCount(forecast).ShouldBe(0);
    }

    [Fact]
    public async Task FetchAsync_ShouldEscapeCityName_WhenGeocoding()
    {
        _host.Http.Expect("https://geocoding-api.open-meteo.com/v1/search")
            .WithExactQueryString("name=S%C3%A3o%20Paulo%26x%3D1&count=1&language=en&format=json")
            .Respond("application/json", """{"generationtime_ms":0.1}""");

        await Source(new OpenMeteoOptions()).FetchAsync(SourceTestHost.Query with { City = "São Paulo&x=1" }, TestContext.Current.CancellationToken);

        _host.Http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnFailure_WhenGeocodingFails()
    {
        _host.Http.When("https://geocoding-api.open-meteo.com/*").Respond(System.Net.HttpStatusCode.InternalServerError);

        var result = await Source(new OpenMeteoOptions()).FetchAsync(SourceTestHost.Query with { City = "Paris" }, TestContext.Current.CancellationToken);

        result.ShouldBe(new SourceFailure("weather answered HTTP 500."));
    }

    [Fact]
    public void Options_ShouldPointAtPublicGeocoding_WhenNotConfigured() =>
        new OpenMeteoOptions().GeocodingAddress.ShouldBe(new Uri("https://geocoding-api.open-meteo.com/"));

    [Fact]
    public async Task FetchAsync_ShouldScoreByClosenessToNow_WhenForecastHasDays()
    {
        _host.Http.When("*").Respond("application/json", SourceTestHost.Fixture("open-meteo-forecast.json"));

        var result = await Source(new OpenMeteoOptions()).FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        result.Items.Select(i => i.Relevance.Value).ShouldBe([1.0, 0.8], tolerance: 1e-9);
    }

    [Fact]
    public void Source_ShouldDescribeItself_WhenListed()
    {
        var source = Source(new OpenMeteoOptions());

        source.Name.ShouldBe("weather");
        source.Categories.ShouldBe(["weather"]);
    }

    [Fact]
    public void Options_ShouldPointAtPublicApi_WhenNotConfigured() =>
        new OpenMeteoOptions().BaseAddress.ShouldBe(new Uri("https://api.open-meteo.com/"));

    [Theory]
    [InlineData(0, "clear sky")]
    [InlineData(2, "partly cloudy")]
    [InlineData(3, "overcast")]
    [InlineData(45, "fog")]
    [InlineData(53, "drizzle")]
    [InlineData(65, "rain")]
    [InlineData(75, "snow")]
    [InlineData(81, "rain showers")]
    [InlineData(86, "snow showers")]
    [InlineData(95, "thunderstorm")]
    [InlineData(42, "unknown conditions")]
    [InlineData(null, "unknown conditions")]
    public void Describe_ShouldNameConditions_WhenGivenWmoCode(int? code, string expected) =>
        WmoWeatherCodes.Describe(code).ShouldBe(expected);

    private static readonly DateTimeOffset FirstForecastDay = new(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);

    private OpenMeteoSource Source(OpenMeteoOptions options) =>
        new(_host.Fetcher, Options.Create(options), new WeatherRelevanceScorer(new FixedTimeProvider(FirstForecastDay)));
}
