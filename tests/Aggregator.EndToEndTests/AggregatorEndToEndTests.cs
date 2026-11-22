using System.Net;
using System.Text.Json;
using Aggregator.Api.IntegrationTests.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

using static Aggregator.Api.IntegrationTests.Infrastructure.AggregationBody;

namespace Aggregator.EndToEndTests;

/// <summary>The whole system as Aspire runs it: AppHost, the API process and its real sources against recorded upstreams.</summary>
public sealed class AggregatorEndToEndTests(AspireAppFixture app) : IClassFixture<AspireAppFixture>
{
    private static readonly string[] AllSources = ["news", "repositories", "weather"];

    [Fact]
    public async Task Readiness_ShouldBeHealthy_WhenTheAppHostHasStarted()
    {
        using var response = await app.Client.GetAsync(ApiRoutes.Readiness, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Aggregation_ShouldReturnItemsFromEverySource_WhenUpstreamsRespond()
    {
        var body = await GetJsonAsync(ApiRoutes.Aggregation);

        Statuses(body).ShouldBe(AllSources.Select(source => KeyValuePair.Create(source, (string?)"Success")), ignoreOrder: true);
        body.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("source").GetString())
            .Distinct()
            .ShouldBe(AllSources, ignoreOrder: true);
    }

    [Fact]
    public async Task Aggregation_ShouldServeLastKnownGoodRepositories_WhenGitHubFailsAfterASuccess()
    {
        const string searchText = "e2e-fallback";
        var live = await GetJsonAsync($"{ApiRoutes.Aggregation}?q={searchText}");
        app.Upstream.Server
            .Given(Request.Create().WithPath(UpstreamServer.GitHubPath).WithParam("q", searchText).UsingGet())
            .AtPriority(UpstreamServer.OverridePriority)
            .RespondWith(Response.Create().WithStatusCode((int)HttpStatusCode.BadGateway));

        var body = await GetJsonAsync($"{ApiRoutes.Aggregation}?q={searchText}");

        Statuses(live)["repositories"].ShouldBe("Success");
        Statuses(body)["repositories"].ShouldBe("Fallback");
        RepositoryIds(body).ShouldBe(RepositoryIds(live), ignoreOrder: true);
    }

    [Fact]
    public async Task Statistics_ShouldCountEachSourceCall_WhenAggregationWasCalled()
    {
        var before = await RequestTotalsAsync();

        await GetJsonAsync($"{ApiRoutes.Aggregation}?q=e2e-statistics");

        var after = await RequestTotalsAsync();
        foreach (var source in AllSources)
        {
            after[source].ShouldBeGreaterThan(before.GetValueOrDefault(source), source);
        }
    }

    private async Task<Dictionary<string, long>> RequestTotalsAsync() =>
        (await GetJsonAsync(ApiRoutes.Statistics)).GetProperty("items").EnumerateArray()
            .GroupBy(row => row.GetProperty("source").GetString()!)
            .ToDictionary(rows => rows.Key, rows => rows.Sum(row => row.GetProperty("totalRequests").GetInt64()));

    private async Task<JsonElement> GetJsonAsync(string url)
    {
        using var response = await app.Client.GetAsync(url, TestContext.Current.CancellationToken);
        return await response.ShouldBeJson(HttpStatusCode.OK);
    }

    private static List<string?> RepositoryIds(JsonElement body) =>
        [.. body.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("source").GetString() == "repositories")
            .Select(item => item.GetProperty("id").GetString())];
}
