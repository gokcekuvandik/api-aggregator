using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Aggregator.Api.IntegrationTests.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Aggregator.Api.IntegrationTests.Sources;

/// <summary>ADR-0006 end to end: parallel fan-out, the fault matrix, the secondary provider, last-known-good data and the circuit breaker.</summary>
public sealed class ResilienceTests : IClassFixture<AggregatorApiFactory>, IClassFixture<UpstreamServer>
{
    private static readonly TimeSpan FastDelay = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan MediumDelay = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan SlowDelay = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan UpstreamDelay = TimeSpan.FromSeconds(2);

    /// <summary>WireMock starts its delay a moment after our stopwatch, so the lower bound allows for timer granularity.</summary>
    private static readonly TimeSpan TimerTolerance = TimeSpan.FromMilliseconds(50);

    private readonly AggregatorApiFactory _factory;
    private readonly UpstreamServer _upstream;

    public ResilienceTests(AggregatorApiFactory factory, UpstreamServer upstream)
    {
        _factory = factory;
        _upstream = upstream;
        _upstream.Server.Reset();
    }

    [Fact]
    [Trait("Category", "Timing")]
    public async Task GetAggregation_ShouldTakeAboutAsLongAsTheSlowestSource_WhenSourcesAnswerInParallel()
    {
        StubDelayed(UpstreamServer.OpenMeteoPath, "open-meteo-forecast.json", FastDelay);
        StubDelayed(UpstreamServer.HackerNewsPath, "hn-search.json", MediumDelay);
        StubDelayed(UpstreamServer.GitHubPath, "github-search.json", SlowDelay);
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());
        await GetAggregationAsync(client);
        var started = Stopwatch.GetTimestamp();

        var body = await GetAggregationAsync(client);

        var elapsed = Stopwatch.GetElapsedTime(started);
        Statuses(body).Values.ShouldAllBe(status => status == "Success");
        elapsed.ShouldBeGreaterThanOrEqualTo(SlowDelay - TimerTolerance);
        elapsed.ShouldBeLessThan(FastDelay + MediumDelay + SlowDelay);
    }

    private static readonly (string Source, string Path)[] Upstreams =
    [
        ("weather", UpstreamServer.OpenMeteoPath),
        ("news", UpstreamServer.HackerNewsPath),
        ("repositories", UpstreamServer.GitHubPath),
    ];

    public static TheoryData<string, string> SourcePaths => new(Upstreams.Select(upstream => (upstream.Source, upstream.Path)));

    public static TheoryData<string, string, int> StatusFaults
    {
        get
        {
            var data = new TheoryData<string, string, int>();
            foreach (var (source, path) in Upstreams)
            {
                data.Add(source, path, 500);
                data.Add(source, path, 429);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(StatusFaults))]
    public async Task GetAggregation_ShouldRetryThenReportOnlyTheFaultySource_WhenItKeepsAnsweringWithError(string source, string path, int status)
    {
        _upstream.StubKeylessSources();
        _upstream.Server.Given(Request.Create().WithPath(path).UsingGet()).AtPriority(UpstreamServer.OverridePriority)
            .RespondWith(Response.Create().WithStatusCode(status));
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());

        var body = await GetAggregationAsync(client);

        AssertOnlyFailed(body, source, $"{source} answered HTTP {status}.");
        _upstream.RequestCount(path, status).ShouldBe(3);
    }

    [Theory]
    [MemberData(nameof(SourcePaths))]
    public async Task GetAggregation_ShouldGiveUpWithinTheTotalTimeout_WhenSourceIsTooSlow(string source, string path)
    {
        _upstream.StubKeylessSources();
        _upstream.Server.Given(Request.Create().WithPath(path).UsingGet()).AtPriority(UpstreamServer.OverridePriority)
            .RespondWith(Response.Create().WithStatusCode(200).WithDelay(UpstreamDelay));
        var settings = _upstream.Settings();
        settings[$"Sources:{source}:Resilience:AttemptTimeout:Timeout"] = "00:00:00.2";
        settings[$"Sources:{source}:Resilience:TotalRequestTimeout:Timeout"] = "00:00:01";
        using var client = _factory.CreateClientWithUpstreams(settings);

        var body = await GetAggregationAsync(client);

        AssertOnlyFailed(body, source, $"{source} timed out.");
        Source(body, source).GetProperty("durationMs").GetDouble().ShouldBeLessThan(UpstreamDelay.TotalMilliseconds);
    }

    [Theory]
    [InlineData("weather", UpstreamServer.OpenWeatherMapPath, "open-meteo", "weather:Athens:2026-11-21T00:00Z")]
    [InlineData("news", UpstreamServer.NewsApiPath, "hacker-news", "news:4242")]
    public async Task GetAggregation_ShouldServeSecondaryProvider_WhenKeyedPrimaryFails(string source, string primaryPath, string provider, string secondaryId)
    {
        _upstream.StubKeylessSources();
        _upstream.Server.Given(Request.Create().WithPath(primaryPath).UsingGet()).RespondWith(Response.Create().WithStatusCode(503));
        using var client = _factory.CreateClientWithUpstreams(KeyedSettings());

        var body = await GetAggregationAsync(client);

        var served = Source(body, source);
        served.GetProperty("status").GetString().ShouldBe("Fallback");
        served.GetProperty("provider").GetString().ShouldBe(provider);
        served.GetProperty("ageSeconds").GetDouble().ShouldBe(0);
        served.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
        Ids(body).ShouldContain(secondaryId);
        _upstream.RequestCount(primaryPath, status: 503).ShouldBe(3);
    }

    [Fact]
    public async Task GetAggregation_ShouldServeLastKnownGoodItemsWithTheirAge_WhenSourceFailsLater()
    {
        _upstream.StubKeylessSources();
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());
        var live = await GetAggregationAsync(client);
        GivenGitHub().AtPriority(UpstreamServer.OverridePriority).RespondWith(Response.Create().WithStatusCode(500));

        var body = await GetAggregationAsync(client);

        var repositories = Source(body, "repositories");
        repositories.GetProperty("status").GetString().ShouldBe("Fallback");
        repositories.GetProperty("ageSeconds").GetDouble().ShouldBeGreaterThanOrEqualTo(0);
        repositories.GetProperty("provider").ValueKind.ShouldBe(JsonValueKind.Null);
        Ids(body).Where(IsRepository).ShouldBe(Ids(live).Where(IsRepository), ignoreOrder: true);
    }

    [Fact]
    public async Task GetAggregation_ShouldStopCallingUpstream_WhenCircuitHasOpened()
    {
        _upstream.StubKeylessSources();
        GivenGitHub().AtPriority(UpstreamServer.OverridePriority).RespondWith(Response.Create().WithStatusCode(500));
        using var client = _factory.CreateClientWithUpstreams(_upstream.Settings());
        await GetAggregationAsync(client);
        await GetAggregationAsync(client);
        var callsBeforeOpenCircuit = _upstream.RequestCount(UpstreamServer.GitHubPath, status: 500);

        var body = await GetAggregationAsync(client);

        Source(body, "repositories").GetProperty("error").GetString().ShouldBe("repositories is temporarily unavailable.");
        _upstream.RequestCount(UpstreamServer.GitHubPath, status: 500).ShouldBe(callsBeforeOpenCircuit);
        callsBeforeOpenCircuit.ShouldBeLessThan(6);
    }

    private static void AssertOnlyFailed(JsonElement body, string source, string expectedError)
    {
        var faulty = Source(body, source);
        faulty.GetProperty("status").GetString().ShouldBe("Failed");
        faulty.GetProperty("error").GetString().ShouldBe(expectedError);
        Statuses(body).Where(pair => pair.Key != source).ShouldAllBe(pair => pair.Value == "Success");
    }

    private void StubDelayed(string path, string fixture, TimeSpan delay) =>
        _upstream.Server.Given(Request.Create().WithPath(path).UsingGet()).RespondWith(UpstreamServer.JsonResponse(fixture).WithDelay(delay));

    private WireMock.Server.IRespondWithAProvider GivenGitHub() =>
        _upstream.Server.Given(Request.Create().WithPath(UpstreamServer.GitHubPath).UsingGet());

    private Dictionary<string, string?> KeyedSettings()
    {
        var settings = _upstream.Settings();
        settings["Sources:weather:ApiKey"] = "owm-test-key";
        settings["Sources:news:ApiKey"] = "newsapi-test-key";
        return settings;
    }

    private static bool IsRepository(string? id) => id!.StartsWith("repositories:", StringComparison.Ordinal);

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
