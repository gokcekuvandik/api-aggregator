using Aggregator.Application.Sources;
using Aggregator.Domain.Items;
using Aggregator.Infrastructure.Sources.Relevance;
using Aggregator.Infrastructure.Sources.Weather.OpenWeatherMap;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace Aggregator.Infrastructure.UnitTests.Sources.Weather;

public sealed class OpenWeatherMapSourceTests : IDisposable
{
    private const string ApiKey = "owm-test-key";

    private readonly SourceTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task FetchAsync_ShouldRequestMetricForecastWithKey_WhenKeyIsConfigured()
    {
        _host.Http.Expect($"{StubHttpClientFactory.DefaultBaseAddress}data/2.5/forecast")
            .WithExactQueryString($"lat=37.98&lon=23.73&units=metric&cnt=16&appid={ApiKey}")
            .Respond("application/json", SourceTestHost.Fixture("openweathermap-forecast.json"));

        await Source(ForecastDays: 2).FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        _host.Http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnOneItemPerReading_WhenForecastHasEntries()
    {
        _host.Http.When("*").Respond("application/json", SourceTestHost.Fixture("openweathermap-forecast.json"));

        var result = await Source().FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        var items = result.ShouldBeOfType<SourceSuccess>().Items;
        items.Select(i => i.Title).ShouldBe(["Athens: clear sky, 12 to 13 °C", "Athens: unknown conditions, 10 to 10 °C"]);
        items[0].PublishedAt.ShouldBe(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));
        items[1].PublishedAt.ShouldBe(new DateTimeOffset(2026, 11, 21, 15, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task FetchAsync_ShouldAskForCityByName_WhenRequestHasCity()
    {
        _host.Http.Expect($"{StubHttpClientFactory.DefaultBaseAddress}data/2.5/forecast")
            .WithExactQueryString($"q=S%C3%A3o%20Paulo&units=metric&cnt=24&appid={ApiKey}")
            .Respond("application/json", """{"list":[{"dt":1795262400,"main":{"temp_min":20,"temp_max":25}}],"city":{"name":"São Paulo"}}""");

        var result = await Source().FetchAsync(SourceTestHost.Query with { City = "São Paulo" }, TestContext.Current.CancellationToken);

        _host.Http.VerifyNoOutstandingExpectation();
        result.Items.ShouldHaveSingleItem().Details.ShouldBe(new WeatherDetails("São Paulo", 20, 25, "unknown conditions"));
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnNoItems_WhenRequestedCityIsNotFound()
    {
        _host.Http.When("*").Respond(System.Net.HttpStatusCode.NotFound, "application/json", """{"cod":"404","message":"city not found"}""");

        var result = await Source().FetchAsync(SourceTestHost.Query with { City = "Nowhere" }, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceSuccess>().Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnFailure_WhenConfiguredLocationIsNotFound()
    {
        _host.Http.When("*").Respond(System.Net.HttpStatusCode.NotFound);

        var result = await Source().FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        result.ShouldBe(new SourceFailure("weather answered HTTP 404."));
    }

    [Fact]
    public async Task FetchAsync_ShouldLabelWithRequestedCity_WhenResponseHasNoCityName()
    {
        _host.Http.When("*").Respond("application/json", """{"list":[{"dt":1795262400,"main":{}}]}""");

        var result = await Source().FetchAsync(SourceTestHost.Query with { City = "Lyon" }, TestContext.Current.CancellationToken);

        result.Items.ShouldHaveSingleItem().Title.ShouldBe("Lyon: unknown conditions");
    }

    [Fact]
    public async Task FetchAsync_ShouldMapDetailsAndScore_WhenForecastHasEntries()
    {
        _host.Http.When("*").Respond("application/json", SourceTestHost.Fixture("openweathermap-forecast.json"));

        var result = await Source().FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        var items = result.ShouldBeOfType<SourceSuccess>().Items;
        items[0].Details.ShouldBe(new WeatherDetails("Athens", 11.8, 12.9, "clear sky"));
        items[0].Relevance.Value.ShouldBe(1.0, tolerance: 1e-9);
        items[1].Relevance.Value.ShouldBe(1 - (3.0 / 120), tolerance: 1e-9);
    }

    [Fact]
    public void Source_ShouldUseWeatherName_WhenItReplacesOpenMeteo()
    {
        var source = Source();

        source.Name.ShouldBe("weather");
        source.Categories.ShouldBe(["weather"]);
    }

    [Fact]
    public void Options_ShouldPointAtPublicApi_WhenNotConfigured() =>
        new OpenWeatherMapOptions().BaseAddress.ShouldBe(new Uri("https://api.openweathermap.org/"));

    private static readonly DateTimeOffset FirstReading = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private OpenWeatherMapSource Source(int ForecastDays = 3) =>
        new(_host.Fetcher,
            Options.Create(new OpenWeatherMapOptions { ApiKey = ApiKey, ForecastDays = ForecastDays }),
            new WeatherRelevanceScorer(new FixedTimeProvider(FirstReading)));
}
