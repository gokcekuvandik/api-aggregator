using System.Net;
using Aggregator.Api.IntegrationTests.Infrastructure;

namespace Aggregator.Api.IntegrationTests.CrossCutting;

/// <summary>Requests are validated before the handler runs, every rule reports at once, and a rejected request never reaches a source.</summary>
public sealed class ValidationInterceptorTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    [Fact]
    public async Task InvalidRequest_ShouldReportFieldAndRegistryErrorsTogether_WhenBothFail()
    {
        using var client = factory.CreateClientWith(StubDataSource.InState("news", "Success"));

        using var response = await client.GetAsync($"{ApiRoutes.Aggregation}?pageSize=0&sources=nope", TestContext.Current.CancellationToken);

        var problem = await response.ShouldBeProblem(HttpStatusCode.BadRequest);
        var errors = problem.GetProperty("errors");
        errors.GetProperty("pageSize")[0].GetString().ShouldBe("The field pageSize must be between 1 and 100.");
        errors.GetProperty("sources")[0].GetString().ShouldBe("Unknown source 'nope'. Known sources: news.");
    }

    [Theory]
    [InlineData(ApiRoutes.Aggregation)]
    [InlineData(ApiRoutes.AggregationStream)]
    public async Task InvalidRequest_ShouldNotReachASource_WhenSourcesAreUnknown(string route)
    {
        var source = new RecordingDataSource("news");
        using var client = factory.CreateClientWith(source);

        using var response = await client.GetAsync($"{route}?sources=nope&sortBy=popularity", TestContext.Current.CancellationToken);

        var problem = await response.ShouldBeProblem(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").EnumerateObject().Select(error => error.Name).ShouldBe(["sources", "sortBy"], ignoreOrder: true);
        source.LastQuery.ShouldBeNull();
    }

    public static TheoryData<string, string, string> Messages => new()
    {
        { $"q={new string('a', 201)}", "q", "The field q must be a string with a maximum length of 200." },
        { $"city={new string('a', 101)}", "city", "The field city must be a string with a maximum length of 100." },
        { "page=0", "page", "The field page must be between 1 and 2147483647." },
        { "from=2026-11-21T00:00:00Z&to=2026-11-20T00:00:00Z", "from", "The field from must not be later than to." },
        { "sortOrder=up", "sortOrder", "The field sortOrder must be one of: desc, asc." },
        { $"sortBy={new string('a', 51)}", "sortBy", "The field sortBy must be a string with a maximum length of 50." },
    };

    [Theory]
    [MemberData(nameof(Messages))]
    public async Task InvalidRequest_ShouldKeepMessageText_WhenOneRuleFails(string query, string key, string message)
    {
        using var client = factory.CreateClientWith(StubDataSource.InState("news", "Success"));

        using var response = await client.GetAsync($"{ApiRoutes.Aggregation}?{query}", TestContext.Current.CancellationToken);

        var problem = await response.ShouldBeProblem(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").GetProperty(key).EnumerateArray().Select(error => error.GetString()).ShouldBe([message]);
    }
}
