using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Aggregator.Api.IntegrationTests.Infrastructure;
using Aggregator.Infrastructure.Sources;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Aggregator.Api.IntegrationTests.Sources;

/// <summary>The real adapters, end to end through the API, against recorded upstream payloads.</summary>
public sealed class ExternalSourceTests : IClassFixture<AggregatorApiFactory>, IClassFixture<UpstreamServer>
{
    private const string WeatherKey = "owm-secret-3f9a";
    private const string NewsKey = "newsapi-secret-7c21";

    private readonly AggregatorApiFactory _factory;
    private readonly UpstreamServer _upstream;

    public ExternalSourceTests(AggregatorApiFactory factory, UpstreamServer upstream)
    {
        _factory = factory;
        _upstream = upstream;
        _upstream.Server.Reset();
    }

    [Fact]
    public async Task GetSources_ShouldListThreeSources_WhenDefaultsAreRegistered()
    {
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());

        using var response = await client.GetAsync(ApiRoutes.Sources, TestContext.Current.CancellationToken);

        var items = (await response.ShouldBeJson(HttpStatusCode.OK)).GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("name").GetString()).ShouldBe(["weather", "news", "repositories"]);
    }

    [Fact]
    public async Task GetAggregation_ShouldMergeEveryKeylessSource_WhenUpstreamsAnswer()
    {
        _upstream.StubKeylessSources();
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());

        var body = await GetAggregationAsync(client);

        Statuses(body).ShouldBe(new Dictionary<string, string?> { ["weather"] = "Success", ["news"] = "Success", ["repositories"] = "Success" });
        Ids(body).ShouldBe(["weather:Athens:2026-11-21T00:00Z", "weather:Athens:2026-11-22T00:00Z", "news:4242", "news:4243", "repositories:17620347", "repositories:2"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetAggregation_ShouldUseKeyedProviders_WhenKeysAreConfigured()
    {
        _upstream.Server.Given(Request.Create().WithPath(UpstreamServer.OpenWeatherMapPath).WithParam("appid", WeatherKey).UsingGet())
            .RespondWith(UpstreamServer.JsonResponse("openweathermap-forecast.json"));
        _upstream.Server.Given(Request.Create().WithPath(UpstreamServer.NewsApiPath).WithHeader("X-Api-Key", NewsKey).UsingGet())
            .RespondWith(UpstreamServer.JsonResponse("newsapi-everything.json"));
        _upstream.StubJson(UpstreamServer.GitHubPath, "github-search.json");
        using var client = _factory.CreateClientWithUpstreams(KeyedSettings());

        var body = await GetAggregationAsync(client);

        Statuses(body).Values.ShouldAllBe(status => status == "Success");
        Ids(body).ShouldContain("news:https://example.com/net11");
        _upstream.RequestCount(UpstreamServer.OpenMeteoPath).ShouldBe(0);
        _upstream.RequestCount(UpstreamServer.HackerNewsPath).ShouldBe(0);
    }

    [Fact]
    public async Task GetAggregation_ShouldRetryThenReportFailure_WhenUpstreamKeepsFailing()
    {
        _upstream.StubKeylessSources();
        GivenGitHub().AtPriority(UpstreamServer.OverridePriority).RespondWith(Response.Create().WithStatusCode(500));
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());

        var body = await GetAggregationAsync(client);

        var repositories = Source(body, "repositories");
        repositories.GetProperty("status").GetString().ShouldBe("Failed");
        repositories.GetProperty("error").GetString().ShouldBe("repositories answered HTTP 500.");
        _upstream.RequestCount(UpstreamServer.GitHubPath).ShouldBe(3);
        Source(body, "news").GetProperty("status").GetString().ShouldBe("Success");
    }

    [Fact]
    public async Task GetAggregation_ShouldWaitForRetryAfter_WhenUpstreamAnswersTooManyRequests()
    {
        _upstream.StubKeylessSources();
        GivenGitHub().AtPriority(UpstreamServer.OverridePriority)
            .InScenario("throttled").WillSetStateTo("open")
            .RespondWith(Response.Create().WithStatusCode(429).WithHeader("Retry-After", "1"));
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());
        var started = Stopwatch.GetTimestamp();

        var body = await GetAggregationAsync(client);

        Stopwatch.GetElapsedTime(started).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(900));
        Source(body, "repositories").GetProperty("status").GetString().ShouldBe("Success");
        _upstream.RequestCount(UpstreamServer.GitHubPath).ShouldBe(2);
    }

    [Fact]
    public async Task GetAggregation_ShouldRevalidateRepositories_WhenCalledTwice()
    {
        _upstream.StubKeylessSources();
        _upstream.StubJson(UpstreamServer.GitHubPath, "github-search.json", priority: UpstreamServer.OverridePriority + 1, ("ETag", "\"gh-1\""));
        _upstream.Server.Given(Request.Create().WithPath(UpstreamServer.GitHubPath).WithHeader("If-None-Match", "\"gh-1\"").UsingGet())
            .AtPriority(UpstreamServer.OverridePriority)
            .RespondWith(Response.Create().WithStatusCode(304).WithHeader("ETag", "\"gh-1\""));
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());

        var first = await GetAggregationAsync(client);
        var second = await GetAggregationAsync(client);

        Ids(second).ShouldBe(Ids(first));
        _upstream.RequestCount(UpstreamServer.GitHubPath, status: 304).ShouldBe(1);
    }

    [Fact]
    public async Task GetAggregation_ShouldNotLogApiKeys_WhenKeyedProvidersAreCalled()
    {
        _upstream.StubJson(UpstreamServer.OpenWeatherMapPath, "openweathermap-forecast.json");
        _upstream.StubJson(UpstreamServer.NewsApiPath, "newsapi-everything.json");
        _upstream.StubJson(UpstreamServer.GitHubPath, "github-search.json");
        var logs = new CapturingLoggerProvider();
        using var client = _factory.CreateClientWithUpstreams(KeyedSettings(), logs: logs);

        await GetAggregationAsync(client);

        logs.Entries.ShouldContain(entry => entry.Category.StartsWith("System.Net.Http.HttpClient.weather", StringComparison.Ordinal));
        logs.Entries.ShouldAllBe(entry => !entry.AllText.Contains(WeatherKey) && !entry.AllText.Contains(NewsKey));
    }

    [Fact]
    public async Task FourthSource_ShouldBeListedAndAggregated_WhenRegisteredWithOneLine()
    {
        _upstream.StubKeylessSources();
        _upstream.Server.Given(Request.Create().WithPath(FakeSource.Path).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("""["one","two"]"""));
        var settings = _upstream.Settings();
        settings["Sources:fake:BaseAddress"] = _upstream.BaseAddress;
        using var client = _factory.CreateClientWithUpstreams(settings, services => services.AddDataSource<FakeSource, FakeSourceOptions>(FakeSource.SourceName));

        using var sources = await client.GetAsync(ApiRoutes.Sources, TestContext.Current.CancellationToken);
        var body = await GetAggregationAsync(client);

        (await sources.ShouldBeJson(HttpStatusCode.OK)).GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("name").GetString()).ShouldBe(["weather", "news", "repositories", "fake"]);
        Ids(body).ShouldContain("fake:0");
        Ids(body).ShouldContain("fake:1");
    }

    private WireMock.Server.IRespondWithAProvider GivenGitHub() =>
        _upstream.Server.Given(Request.Create().WithPath(UpstreamServer.GitHubPath).UsingGet());

    [Fact]
    public async Task GetAggregation_ShouldGeocodeCityThenForecastThere_WhenCityIsGiven()
    {
        _upstream.Server.Given(Request.Create().WithPath(UpstreamServer.GeocodingPath).WithParam("name", "Paris").UsingGet())
            .RespondWith(UpstreamServer.JsonResponse("open-meteo-geocoding.json"));
        _upstream.Server.Given(Request.Create().WithPath(UpstreamServer.OpenMeteoPath).WithParam("latitude", "48.85341").WithParam("longitude", "2.3488").UsingGet())
            .RespondWith(UpstreamServer.JsonResponse("open-meteo-forecast.json"));
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());

        using var response = await client.GetAsync($"{ApiRoutes.Aggregation}?city=Paris&sources=weather", TestContext.Current.CancellationToken);

        var body = await response.ShouldBeJson(HttpStatusCode.OK);
        Statuses(body).ShouldBe(new Dictionary<string, string?> { ["weather"] = "Success" });
        Ids(body).ShouldAllBe(id => id!.StartsWith("weather:Paris:", StringComparison.Ordinal));
        body.GetProperty("items")[0].GetProperty("details").GetProperty("location").GetString().ShouldBe("Paris");
    }

    [Fact]
    public async Task GetAggregation_ShouldReturnNoWeatherItemsRatherThan503_WhenCityIsUnknown()
    {
        _upstream.Server.Given(Request.Create().WithPath(UpstreamServer.GeocodingPath).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithHeader("Content-Type", "application/json").WithBody("""{"generationtime_ms":0.1}"""));
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());

        using var response = await client.GetAsync($"{ApiRoutes.Aggregation}?city=Nowhere&sources=weather", TestContext.Current.CancellationToken);

        var body = await response.ShouldBeJson(HttpStatusCode.OK);
        Statuses(body).ShouldBe(new Dictionary<string, string?> { ["weather"] = "Success" });
        Ids(body).ShouldBeEmpty();
        _upstream.Server.LogEntries.ShouldNotContain(entry => entry.RequestMessage!.Path == UpstreamServer.OpenMeteoPath);
    }

    private Dictionary<string, string?> KeyedSettings()
    {
        var settings = _upstream.Settings();
        settings["Sources:weather:ApiKey"] = WeatherKey;
        settings["Sources:news:ApiKey"] = NewsKey;
        return settings;
    }

    private static async Task<JsonElement> GetAggregationAsync(HttpClient client)
    {
        using var response = await client.GetAsync(ApiRoutes.Aggregation, TestContext.Current.CancellationToken);
        return await response.ShouldBeJson(HttpStatusCode.OK);
    }

    private static JsonElement Source(JsonElement body, string name) =>
        body.GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);

    private static Dictionary<string, string?> Statuses(JsonElement body) =>
        body.GetProperty("sources").EnumerateArray().ToDictionary(s => s.GetProperty("name").GetString()!, s => s.GetProperty("status").GetString());

    private static List<string?> Ids(JsonElement body) =>
        [.. body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString())];
}
