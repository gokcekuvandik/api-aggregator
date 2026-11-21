using System.Net;
using System.Text.Json;
using Aggregator.Api.IntegrationTests.Infrastructure;
using Aggregator.Application.Sources;

namespace Aggregator.Api.IntegrationTests.Endpoints;

/// <summary><c>GET /api/v1/aggregation</c> contract and status codes.</summary>
public sealed class AggregationEndpointTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    private const string Url = ApiRoutes.Aggregation;

    /// <summary>Source states mapped to the HTTP status.</summary>
    public static TheoryData<string[], HttpStatusCode> DecisionTable => new()
    {
        { ["Success", "Success"], HttpStatusCode.OK },
        { ["Success", "Failed"], HttpStatusCode.OK },
        { ["Cached", "Failed"], HttpStatusCode.OK },
        { ["Fallback", "Failed"], HttpStatusCode.OK },
        { ["Fallback"], HttpStatusCode.OK },
        { ["Failed", "Failed"], HttpStatusCode.ServiceUnavailable },
        { [], HttpStatusCode.ServiceUnavailable },
    };

    [Theory]
    [MemberData(nameof(DecisionTable))]
    public async Task Get_ShouldReturnStatusAndReportEachSource_WhenSourcesAreInGivenStates(string[] states, HttpStatusCode expected)
    {
        var sources = states.Select((state, i) => (IDataSource)StubDataSource.InState($"s{i}", state)).ToArray();
        using var client = factory.CreateClientWith(sources);

        using var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        var body = expected == HttpStatusCode.OK
            ? await response.ShouldBeJson(expected)
            : await response.ShouldBeProblem(expected);
        var reported = body.GetProperty("sources").EnumerateArray().ToList();
        reported.Select(s => s.GetProperty("name").GetString()).ShouldBe(sources.Select(s => s.Name));
        reported.Select(s => s.GetProperty("status").GetString()).ShouldBe(states);
    }

    [Fact]
    public async Task Get_ShouldDescribeEachSource_WhenOneSucceedsAndOneFails()
    {
        using var client = factory.CreateClientWith(
            StubDataSource.InState("news", "Success"),
            StubDataSource.InState("weather", "Failed"));

        using var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        var sources = (await response.ShouldBeJson(HttpStatusCode.OK)).GetProperty("sources");
        var news = sources[0];
        news.GetProperty("durationMs").GetDouble().ShouldBeGreaterThanOrEqualTo(0);
        news.GetProperty("error").ValueKind.ShouldBe(JsonValueKind.Null);
        sources[1].GetProperty("error").GetString().ShouldBe("weather is unavailable");
    }

    [Fact]
    public async Task Get_ShouldReportFallbackAge_WhenSourceServedFallback()
    {
        using var client = factory.CreateClientWith(StubDataSource.InState("news", "Fallback"));

        using var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        var source = (await response.ShouldBeJson(HttpStatusCode.OK)).GetProperty("sources")[0];
        source.GetProperty("ageSeconds").GetDouble().ShouldBe(90);
    }

    [Fact]
    public async Task Get_ShouldReturnItemsAndPaging_WhenPageIsRequested()
    {
        using var client = factory.CreateClientWith(StubDataSource.InState("news", "Success", itemCount: 5));

        using var response = await client.GetAsync($"{Url}?page=2&pageSize=2", TestContext.Current.CancellationToken);

        var body = await response.ShouldBeJson(HttpStatusCode.OK);
        body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).ShouldBe(["news-3", "news-4"]);
        var paging = body.GetProperty("paging");
        paging.GetProperty("page").GetInt32().ShouldBe(2);
        paging.GetProperty("pageSize").GetInt32().ShouldBe(2);
        paging.GetProperty("totalCount").GetInt32().ShouldBe(5);
    }

    [Fact]
    public async Task Get_ShouldUseDefaultPaging_WhenNoPagingIsGiven()
    {
        using var client = factory.CreateClientWith(StubDataSource.InState("news", "Success"));

        using var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        var paging = (await response.ShouldBeJson(HttpStatusCode.OK)).GetProperty("paging");
        paging.GetProperty("page").GetInt32().ShouldBe(1);
        paging.GetProperty("pageSize").GetInt32().ShouldBe(20);
    }

    [Theory]
    [InlineData("pageSize=1")]
    [InlineData("pageSize=100")]
    [InlineData("page=1")]
    public async Task Get_ShouldReturn200_WhenPagingIsOnTheBoundary(string query)
    {
        using var client = factory.CreateClientWith(StubDataSource.InState("news", "Success"));

        using var response = await client.GetAsync($"{Url}?{query}", TestContext.Current.CancellationToken);

        await response.ShouldBeJson(HttpStatusCode.OK);
    }

    /// <summary>Invalid paging in both environments, which write bad-request errors differently.</summary>
    private static readonly string[] InvalidQueries = ["pageSize=0", "pageSize=101", "page=0", "page=-1", "pageSize=abc"];
    private static readonly string[] Environments = ["Development", "Production"];

    public static TheoryData<string, string> InvalidPaging => new(
        from query in InvalidQueries
        from environment in Environments
        select (query, environment));

    [Theory]
    [MemberData(nameof(InvalidPaging))]
    public async Task Get_ShouldReturn400Problem_WhenPagingIsInvalid(string query, string environment)
    {
        using var client = factory.CreateClientIn(environment, StubDataSource.InState("news", "Success"));

        using var response = await client.GetAsync($"{Url}?{query}", TestContext.Current.CancellationToken);

        await response.ShouldBeProblem(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_ShouldReturn500ProblemWithoutDetails_WhenSourceThrowsUnexpectedly()
    {
        using var client = factory.CreateProductionClientWith(new ThrowingDataSource());

        using var response = await client.GetAsync(Url, TestContext.Current.CancellationToken);

        var problem = await response.ShouldBeProblem(HttpStatusCode.InternalServerError);
        problem.GetRawText().ShouldNotContain("hunter2");
        problem.TryGetProperty("exception", out _).ShouldBeFalse();
    }
}
