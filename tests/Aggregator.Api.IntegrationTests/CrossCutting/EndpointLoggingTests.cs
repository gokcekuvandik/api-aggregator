using System.Net;
using Aggregator.Api.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Aggregator.Api.IntegrationTests.CrossCutting;

/// <summary>A rejected or degraded API call is logged once with its endpoint name and the names of the invalid fields, never their values.</summary>
public sealed class EndpointLoggingTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    private const string InterceptorCategory = "Aggregator.Api.Http.EndpointLoggingInterceptor";
    private const string UnknownSource = "secret-source-5f1c";

    [Fact]
    public async Task RejectedRequest_ShouldLogEndpointAndFieldNames_WhenSourceIsUnknown()
    {
        var logs = new CapturingLoggerProvider();
        using var client = factory.CreateClientWithLogs(logs, StubDataSource.InState("news", "Success"));

        using var response = await client.GetAsync($"{ApiRoutes.Aggregation}?sources={UnknownSource}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var entry = logs.InCategory(InterceptorCategory).ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.State["EndpointName"].ShouldBe("GetAggregation");
        entry.State["StatusCode"].ShouldBe("400");
        entry.State["InvalidFields"].ShouldBe("sources");
        entry.AllText.ShouldNotContain(UnknownSource);
    }

    [Fact]
    public async Task RejectedRequest_ShouldLogFieldNamesOnly_WhenPageSizeIsOutOfRange()
    {
        const string hugePageSize = "987654";
        var logs = new CapturingLoggerProvider();
        using var client = factory.CreateClientWithLogs(logs, StubDataSource.InState("news", "Success"));

        using var response = await client.GetAsync($"{ApiRoutes.Aggregation}?pageSize={hugePageSize}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var entry = logs.InCategory(InterceptorCategory).ShouldHaveSingleItem();
        entry.State["InvalidFields"].ShouldBe("pageSize");
        entry.AllText.ShouldNotContain(hugePageSize);
    }

    [Fact]
    public async Task FailedRequest_ShouldLogEndpointAndStatus_WhenEverySourceFails()
    {
        var logs = new CapturingLoggerProvider();
        using var client = factory.CreateClientWithLogs(logs, StubDataSource.InState("news", "Failed"));

        using var response = await client.GetAsync(ApiRoutes.Aggregation, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var entry = logs.InCategory(InterceptorCategory).ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.State["EndpointName"].ShouldBe("GetAggregation");
        entry.State["StatusCode"].ShouldBe("503");
    }

    [Theory]
    [InlineData(ApiRoutes.Aggregation)]
    [InlineData(ApiRoutes.AggregationStream)]
    [InlineData(ApiRoutes.Sources)]
    public async Task SuccessfulRequest_ShouldWriteNoEntry_WhenEndpointSucceeds(string route)
    {
        var logs = new CapturingLoggerProvider();
        using var client = factory.CreateClientWithLogs(logs, StubDataSource.InState("news", "Success"));

        using var response = await client.GetAsync(route, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        logs.InCategory(InterceptorCategory).ShouldBeEmpty();
    }
}
