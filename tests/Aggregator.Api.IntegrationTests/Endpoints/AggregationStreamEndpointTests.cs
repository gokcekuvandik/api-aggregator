using System.Net;
using System.Text.Json;
using Aggregator.Api.IntegrationTests.Infrastructure;

namespace Aggregator.Api.IntegrationTests.Endpoints;

/// <summary><c>GET /api/v1/aggregation/stream</c>: one server-sent event per source as it completes, then <c>done</c>.</summary>
public sealed class AggregationStreamEndpointTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    private static readonly TimeSpan SlowDelay = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task Get_ShouldEmitEachSourceInCompletionOrderThenDone_WhenSourcesFinishAtDifferentTimes()
    {
        using var client = factory.CreateClientWith(
            new DelayedDataSource(StubDataSource.InState("news", "Success", itemCount: 2), SlowDelay),
            StubDataSource.InState("weather", "Fallback"));

        using var response = await client.GetAsync(ApiRoutes.AggregationStream, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
        var events = await ReadEventsAsync(response);
        events.Select(e => e.Type).ShouldBe(["source", "source", "done"]);
        events[0].Data.GetProperty("source").GetProperty("name").GetString().ShouldBe("weather");
        events[0].Data.GetProperty("source").GetProperty("status").GetString().ShouldBe("Fallback");
        events[0].Data.GetProperty("source").GetProperty("ageSeconds").GetDouble().ShouldBe(90);
        events[1].Data.GetProperty("source").GetProperty("name").GetString().ShouldBe("news");
        events[1].Data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ShouldBe(["news-1", "news-2"]);
        events[2].Data.GetProperty("sourceCount").GetInt32().ShouldBe(2);
        events[2].Data.GetProperty("hasData").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Get_ShouldReportNoDataInDone_WhenEverySourceFails()
    {
        using var client = factory.CreateClientWith(StubDataSource.InState("news", "Failed"));

        using var response = await client.GetAsync(ApiRoutes.AggregationStream, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        var events = await ReadEventsAsync(response);
        events[0].Data.GetProperty("source").GetProperty("error").GetString().ShouldBe("news is unavailable");
        events[0].Data.GetProperty("items").GetArrayLength().ShouldBe(0);
        events[^1].Data.GetProperty("hasData").GetBoolean().ShouldBeFalse();
    }

    [Theory]
    [InlineData("sources=nope", "sources")]
    [InlineData("sortBy=stars", "sortBy")]
    public async Task Get_ShouldReturn400Problem_WhenParametersAreInvalid(string query, string key)
    {
        using var client = factory.CreateProductionClientWith(StubDataSource.InState("news", "Success"));

        using var response = await client.GetAsync($"{ApiRoutes.AggregationStream}?{query}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = JsonElement.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        body.GetProperty("errors").TryGetProperty(key, out _).ShouldBeTrue();
    }

    private static async Task<List<(string Type, JsonElement Data)>> ReadEventsAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var events = new List<(string Type, JsonElement Data)>();
        foreach (var block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var lines = block.Split('\n');
            var type = lines.Single(line => line.StartsWith("event: ", StringComparison.Ordinal))["event: ".Length..];
            var data = string.Concat(lines.Where(line => line.StartsWith("data: ", StringComparison.Ordinal)).Select(line => line["data: ".Length..]));
            events.Add((type, JsonElement.Parse(data)));
        }

        return events;
    }
}
