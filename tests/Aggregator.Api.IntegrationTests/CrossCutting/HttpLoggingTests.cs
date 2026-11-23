using System.Net;
using Aggregator.Api.IntegrationTests.Infrastructure;

namespace Aggregator.Api.IntegrationTests.CrossCutting;

/// <summary>One redacted access-log entry per request, none for health probes.</summary>
public sealed class HttpLoggingTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    private const string QueryToken = "query-token-7a2d4b";

    /// <summary>A valid token, so the request also passes through the bearer handler's own logging.</summary>
    private static readonly string HeaderToken = TestTokens.Create(TestTokens.AllScopes);

    [Fact]
    public async Task Request_ShouldWriteOneCombinedEntry_WhenEndpointIsCalled()
    {
        var logs = new CapturingLoggerProvider();
        using var client = factory.CreateClientWithLogs(logs);

        using var response = await client.GetAsync(ApiRoutes.Sources, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var entry = logs.InCategory(LogCategories.HttpLogging).ShouldHaveSingleItem();
        entry.State["Method"].ShouldBe("GET");
        entry.State["Path"].ShouldBe(ApiRoutes.Sources);
        entry.State["StatusCode"].ShouldBe("200");
        entry.State.ShouldContainKey("Duration");
    }

    [Fact]
    public async Task Request_ShouldNotLogTokens_WhenHeaderAndQueryCarryThem()
    {
        var logs = new CapturingLoggerProvider();
        using var client = factory.CreateClientWithLogs(logs);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiRoutes.Sources}?access_token={QueryToken}");
        request.Headers.Authorization = new("Bearer", HeaderToken);

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        logs.InCategory(LogCategories.HttpLogging).ShouldNotBeEmpty();
        logs.Entries.ShouldAllBe(entry => !entry.AllText.Contains(HeaderToken) && !entry.AllText.Contains(QueryToken));
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthProbe_ShouldNotWriteEntry_WhenCalled(string path)
    {
        var logs = new CapturingLoggerProvider();
        using var client = factory.CreateClientWithLogs(logs);

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        logs.InCategory(LogCategories.HttpLogging).ShouldBeEmpty();
    }

    [Fact]
    public async Task Request_ShouldLogStatus500_WhenSourceThrows()
    {
        var logs = new CapturingLoggerProvider();
        using var client = factory.CreateClientWithLogs(logs, new ThrowingDataSource());

        using var response = await client.GetAsync(ApiRoutes.Aggregation, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        logs.InCategory(LogCategories.HttpLogging).ShouldHaveSingleItem().State["StatusCode"].ShouldBe("500");
    }
}
