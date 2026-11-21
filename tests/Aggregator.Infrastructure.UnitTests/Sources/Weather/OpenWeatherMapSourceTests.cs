using Aggregator.Application.Sources;
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
    public void Source_ShouldUseWeatherName_WhenItReplacesOpenMeteo()
    {
        var source = Source();

        source.Name.ShouldBe("weather");
        source.Categories.ShouldBe(["weather"]);
    }

    [Fact]
    public void Options_ShouldPointAtPublicApi_WhenNotConfigured() =>
        new OpenWeatherMapOptions().BaseAddress.ShouldBe(new Uri("https://api.openweathermap.org/"));

    private OpenWeatherMapSource Source(int ForecastDays = 3) =>
        new(_host.Fetcher, Options.Create(new OpenWeatherMapOptions { ApiKey = ApiKey, ForecastDays = ForecastDays }));
}
