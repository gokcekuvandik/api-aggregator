using System.Net;
using Aggregator.Api.IntegrationTests.Infrastructure;
using Aggregator.Application.Statistics;

namespace Aggregator.Api.IntegrationTests.Endpoints;

public sealed class SourcesAndStatisticsEndpointTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    [Fact]
    public async Task GetSources_ShouldListRegisteredSources_WhenSourcesAreRegistered()
    {
        using var client = factory.CreateClientWith(
            StubDataSource.InState("news", "Success"),
            StubDataSource.InState("weather", "Success"));

        using var response = await client.GetAsync(ApiRoutes.Sources, TestContext.Current.CancellationToken);

        var items = (await response.ShouldBeJson(HttpStatusCode.OK)).GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("name").GetString()).ShouldBe(["news", "weather"]);
        items[0].GetProperty("categories").EnumerateArray().Select(c => c.GetString()).ShouldBe(["news"]);
    }

    [Fact]
    public async Task GetStatistics_ShouldReturnSnapshotPerSource_WhenStatisticsExist()
    {
        var reader = new StubStatisticsReader(new ApiStatisticsSnapshot(
            Source: "news", TotalRequests: 4, AverageDuration: TimeSpan.FromMilliseconds(125), CacheHits: 2, Failures: 1));
        using var client = factory.CreateClientWith(reader);

        using var response = await client.GetAsync(ApiRoutes.Statistics, TestContext.Current.CancellationToken);

        var item = (await response.ShouldBeJson(HttpStatusCode.OK)).GetProperty("items")[0];
        item.GetProperty("source").GetString().ShouldBe("news");
        item.GetProperty("totalRequests").GetInt64().ShouldBe(4);
        item.GetProperty("averageMs").GetDouble().ShouldBe(125);
        item.GetProperty("cacheHits").GetInt64().ShouldBe(2);
        item.GetProperty("failures").GetInt64().ShouldBe(1);
    }

    [Fact]
    public async Task GetStatistics_ShouldReturnEmptyList_WhenNothingWasRecorded()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(ApiRoutes.Statistics, TestContext.Current.CancellationToken);

        (await response.ShouldBeJson(HttpStatusCode.OK)).GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    private sealed class StubStatisticsReader(params ApiStatisticsSnapshot[] snapshots) : IStatisticsReader
    {
        public IReadOnlyList<ApiStatisticsSnapshot> GetSnapshots() => snapshots;
    }
}
