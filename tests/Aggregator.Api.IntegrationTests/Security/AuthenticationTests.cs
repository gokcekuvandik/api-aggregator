using System.Net;
using Aggregator.Api.IntegrationTests.Infrastructure;

namespace Aggregator.Api.IntegrationTests.Security;

/// <summary>Every <c>/api/v1</c> route needs a valid bearer token; health and OpenAPI stay anonymous.</summary>
public sealed class AuthenticationTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    public static TheoryData<string> AnonymousRoutes => [ApiRoutes.Liveness, ApiRoutes.Readiness, ApiRoutes.OpenApiDocument];

    public static TheoryData<string> RejectedTokens =>
    [
        nameof(TokenSpec.Lifetime),
        nameof(TokenSpec.Audience),
        nameof(TokenSpec.Issuer),
        nameof(TokenSpec.UseUntrustedKey),
        "Malformed",
    ];

    [Theory]
    [MemberData(nameof(ApiRoutes.Protected), MemberType = typeof(ApiRoutes))]
    public async Task Get_ShouldReturn401ProblemWithBearerChallenge_WhenNoTokenIsSent(string route)
    {
        using var client = factory.SecuredHost.CreateClientWithToken(null);

        using var response = await client.GetAsync(route, TestContext.Current.CancellationToken);

        await response.ShouldBeProblem(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Select(h => h.Scheme).ShouldBe(["Bearer"]);
    }

    [Theory]
    [MemberData(nameof(RejectedTokens))]
    public async Task Get_ShouldReturn401Problem_WhenTokenIsInvalid(string flaw)
    {
        var token = flaw switch
        {
            nameof(TokenSpec.Lifetime) => TestTokens.Create(new TokenSpec { Lifetime = TimeSpan.FromHours(-1) }),
            nameof(TokenSpec.Audience) => TestTokens.Create(new TokenSpec { Audience = "someone-else" }),
            nameof(TokenSpec.Issuer) => TestTokens.Create(new TokenSpec { Issuer = "untrusted-issuer" }),
            nameof(TokenSpec.UseUntrustedKey) => TestTokens.Create(new TokenSpec { UseUntrustedKey = true }),
            _ => "not-a-jwt",
        };
        using var client = factory.SecuredHost.CreateClientWithToken(token);

        using var response = await client.GetAsync(ApiRoutes.Sources, TestContext.Current.CancellationToken);

        await response.ShouldBeProblem(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(AnonymousRoutes))]
    public async Task Get_ShouldReturn200_WhenAnonymousRouteIsCalledWithoutToken(string route)
    {
        using var client = factory.SecuredHost.CreateClientWithToken(null);

        using var response = await client.GetAsync(route, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_ShouldReturn200_WhenTokenIsValid()
    {
        using var client = factory.SecuredHost.CreateClient();

        using var response = await client.GetAsync(ApiRoutes.Sources, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_ShouldReturn401_WhenTokenExpiredMoreThanAMinuteAgo()
    {
        var token = TestTokens.Create(new TokenSpec { Lifetime = TimeSpan.FromMinutes(-2) });
        using var client = factory.SecuredHost.CreateClientWithToken(token);

        using var response = await client.GetAsync(ApiRoutes.Sources, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Authentication:Schemes:Bearer:TokenValidationParameters:ValidateLifetime")]
    [InlineData("Authentication:Schemes:Bearer:ValidateLifetime")]
    public async Task Get_ShouldStillRejectExpiredToken_WhenConfigurationTriesToTurnLifetimeChecksOff(string setting)
    {
        var token = TestTokens.Create(new TokenSpec { Lifetime = TimeSpan.FromHours(-1) });
        using var host = factory.HostWith(new Dictionary<string, string?> { [setting] = "false" });
        using var client = host.CreateClientWithToken(token);

        using var response = await client.GetAsync(ApiRoutes.Sources, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void Startup_ShouldSucceed_WhenAnAuthorityReplacesLocalSigningKeys()
    {
        using var host = factory.HostWith(new Dictionary<string, string?>
        {
            ["Authentication:Schemes:Bearer:Authority"] = "https://login.example.test/tenant/v2.0",
            ["Authentication:Schemes:Bearer:ValidIssuer"] = "",
        });

        Should.NotThrow(() => host.CreateClient().Dispose());
    }

    [Theory]
    [InlineData("Authentication:Schemes:Bearer:ValidAudiences:0")]
    [InlineData("Authentication:Schemes:Bearer:ValidIssuer")]
    public void Startup_ShouldFail_WhenTokenValidationIsNotConfigured(string blankedSetting)
    {
        using var host = factory.HostWith(new Dictionary<string, string?> { [blankedSetting] = "" });

        var error = Should.Throw<Exception>(() => host.CreateClient());

        error.ToString().ShouldContain("dotnet user-jwts create");
    }
}
