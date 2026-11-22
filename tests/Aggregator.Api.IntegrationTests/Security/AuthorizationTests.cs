using System.Net;
using Aggregator.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregator.Api.IntegrationTests.Security;

/// <summary><c>aggregation:read</c> covers aggregation and sources; <c>stats:read</c> covers statistics.</summary>
public sealed class AuthorizationTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    public static TheoryData<string, string, HttpStatusCode> ScopeMatrix => new()
    {
        { TestTokens.AggregationRead, ApiRoutes.Aggregation, HttpStatusCode.OK },
        { TestTokens.AggregationRead, ApiRoutes.AggregationStream, HttpStatusCode.OK },
        { TestTokens.AggregationRead, ApiRoutes.Sources, HttpStatusCode.OK },
        { TestTokens.AggregationRead, ApiRoutes.Statistics, HttpStatusCode.Forbidden },
        { TestTokens.StatsRead, ApiRoutes.Aggregation, HttpStatusCode.Forbidden },
        { TestTokens.StatsRead, ApiRoutes.AggregationStream, HttpStatusCode.Forbidden },
        { TestTokens.StatsRead, ApiRoutes.Sources, HttpStatusCode.Forbidden },
        { TestTokens.StatsRead, ApiRoutes.Statistics, HttpStatusCode.OK },
    };

    [Theory]
    [MemberData(nameof(ScopeMatrix))]
    public async Task Get_ShouldAllowOnlyGrantedRoutes_WhenTokenHasOneScope(string scope, string route, HttpStatusCode expected)
    {
        using var client = factory.SecuredHost.CreateClientWithToken(TestTokens.Create(scope));

        using var response = await client.GetAsync(route, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(ApiRoutes.Protected), MemberType = typeof(ApiRoutes))]
    public async Task Get_ShouldReturn403Problem_WhenTokenHasNoScope(string route)
    {
        using var client = factory.SecuredHost.CreateClientWithToken(TestTokens.Create());

        using var response = await client.GetAsync(route, TestContext.Current.CancellationToken);

        await response.ShouldBeProblem(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("scp")]
    public async Task Get_ShouldAcceptScopes_WhenIdentityProviderSendsOneSpaceSeparatedClaim(string claimType)
    {
        var token = TestTokens.Create(new TokenSpec { Scopes = ["openid aggregation:read stats:read"], ScopeClaim = claimType });
        using var client = factory.SecuredHost.CreateClientWithToken(token);

        using var statistics = await client.GetAsync(ApiRoutes.Statistics, TestContext.Current.CancellationToken);
        using var sources = await client.GetAsync(ApiRoutes.Sources, TestContext.Current.CancellationToken);

        statistics.StatusCode.ShouldBe(HttpStatusCode.OK);
        sources.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_ShouldReturn403_WhenScopeOnlyResemblesTheRequiredOne()
    {
        using var client = factory.SecuredHost.CreateClientWithToken(TestTokens.Create("stats:read-all", "STATS:READ"));

        using var response = await client.GetAsync(ApiRoutes.Statistics, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public void ApiEndpoints_ShouldEachRequireAScopePolicy_WhenMapped()
    {
        var host = factory.SecuredHost;
        _ = host.Server;

        var endpoints = host.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true)
            .ToList();

        endpoints.Count.ShouldBe(4, "A new /api endpoint must be added to this count and to the scope matrix above.");
        endpoints.ShouldAllBe(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Any(data => TestTokens.AllScopes.Contains(data.Policy)));
    }
}
