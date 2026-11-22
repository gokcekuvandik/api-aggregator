using System.Globalization;
using System.Net;
using Aggregator.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Aggregator.Api.IntegrationTests.Security;

/// <summary>A token bucket per caller on <c>/api/v1</c>: the JWT subject, or the client IP for a token without one.</summary>
public sealed class RateLimitingTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    private const int TokenLimit = 2;

    /// <summary>Two requests per caller, and no refill within any test's lifetime.</summary>
    private static readonly Dictionary<string, string?> TightLimit = new()
    {
        ["RateLimiting:TokenLimit"] = TokenLimit.ToString(CultureInfo.InvariantCulture),
        ["RateLimiting:TokensPerPeriod"] = "1",
        ["RateLimiting:ReplenishmentPeriod"] = "00:01:00",
    };

    [Fact]
    public async Task Get_ShouldReturn429ProblemWithRetryAfter_WhenCallerExceedsTheBucket()
    {
        using var host = factory.HostWith(TightLimit);
        using var client = host.CreateClient();

        var statuses = await SendAsync(client, ApiRoutes.Sources, TokenLimit);
        using var rejected = await client.GetAsync(ApiRoutes.Sources, TestContext.Current.CancellationToken);

        statuses.ShouldAllBe(status => status == HttpStatusCode.OK);
        await rejected.ShouldBeProblem(HttpStatusCode.TooManyRequests);
        var retryAfter = rejected.Headers.RetryAfter.ShouldNotBeNull().Delta.ShouldNotBeNull();
        retryAfter.ShouldBeInRange(TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Get_ShouldKeepCallersApart_WhenSubjectsDiffer()
    {
        using var host = factory.HostWith(TightLimit);
        using var first = ClientFor(host, "first-caller");
        using var second = ClientFor(host, "second-caller");

        await SendAsync(first, ApiRoutes.Sources, TokenLimit + 1);
        using var response = await second.GetAsync(ApiRoutes.Sources, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_ShouldShareTheClientAddressBucket_WhenTokensHaveNoSubject()
    {
        using var host = factory.HostWith(TightLimit);
        using var first = ClientFor(host, subject: null);
        using var second = ClientFor(host, subject: null);

        await SendAsync(first, ApiRoutes.Sources, TokenLimit);
        using var response = await second.GetAsync(ApiRoutes.Sources, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData(ApiRoutes.Liveness)]
    [InlineData(ApiRoutes.OpenApiDocument)]
    public async Task Get_ShouldNotLimit_WhenRouteIsOutsideTheApi(string route)
    {
        using var host = factory.HostWith(TightLimit);
        using var client = host.CreateClient();

        var statuses = await SendAsync(client, route, TokenLimit + 2);

        statuses.ShouldAllBe(status => status == HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("RateLimiting:TokenLimit", "0")]
    [InlineData("RateLimiting:TokensPerPeriod", "0")]
    [InlineData("RateLimiting:ReplenishmentPeriod", "00:00:00")]
    public void Startup_ShouldFail_WhenRateLimitSettingIsInvalid(string key, string value)
    {
        using var host = factory.HostWith(new Dictionary<string, string?> { [key] = value });

        var error = Should.Throw<Exception>(() => host.CreateClient());

        error.ToString().ShouldContain("RateLimiting");
    }

    private static HttpClient ClientFor(WebApplicationFactory<Program> host, string? subject) =>
        host.CreateClientWithToken(TestTokens.Create(new TokenSpec { Subject = subject }));

    private static async Task<IReadOnlyList<HttpStatusCode>> SendAsync(HttpClient client, string route, int count)
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < count; i++)
        {
            using var response = await client.GetAsync(route, TestContext.Current.CancellationToken);
            statuses.Add(response.StatusCode);
        }

        return statuses;
    }
}
