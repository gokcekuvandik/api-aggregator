using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>Signs HS256 tokens the way <c>dotnet user-jwts</c> does, with a key that exists only in tests.</summary>
internal static class TestTokens
{
    public const string Issuer = "aggregator-tests";
    public const string Audience = "aggregator-api";
    public const string AggregationRead = "aggregation:read";
    public const string StatsRead = "stats:read";
    public const string DefaultSubject = "reviewer";

    public static readonly string[] AllScopes = [AggregationRead, StatsRead];

    private static readonly byte[] SigningKey = Encoding.UTF8.GetBytes("integration-tests-only-signing-key-0123456789");
    private static readonly byte[] UntrustedKey = Encoding.UTF8.GetBytes("a-different-key-the-api-does-not-trust-987654");

    /// <summary>The settings <c>dotnet user-jwts</c> writes, pointed at the test key.</summary>
    public static IReadOnlyDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>
    {
        ["Authentication:Schemes:Bearer:ValidIssuer"] = Issuer,
        ["Authentication:Schemes:Bearer:ValidAudiences:0"] = Audience,
        ["Authentication:Schemes:Bearer:SigningKeys:0:Issuer"] = Issuer,
        ["Authentication:Schemes:Bearer:SigningKeys:0:Value"] = Convert.ToBase64String(SigningKey),
    };

    /// <summary>A valid token carrying each scope as its own claim, as <c>dotnet user-jwts create --scope</c> does.</summary>
    public static string Create(params string[] scopes) => Create(new TokenSpec { Scopes = scopes });

    public static string Create(TokenSpec spec)
    {
        var claims = new List<Claim>();
        if (spec.Subject is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, spec.Subject));
        }

        claims.AddRange(spec.Scopes.Select(scope => new Claim(spec.ScopeClaim, scope)));

        var now = DateTime.UtcNow;
        var issuedAt = spec.Lifetime < TimeSpan.Zero ? now.Add(spec.Lifetime * 2) : now.AddMinutes(-1);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = spec.Issuer,
            Audience = spec.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = now.Add(spec.Lifetime),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(spec.UseUntrustedKey ? UntrustedKey : SigningKey), SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }
}

/// <summary>What a test token says; the defaults describe a valid token with both scopes.</summary>
internal sealed record TokenSpec
{
    public IReadOnlyList<string> Scopes { get; init; } = TestTokens.AllScopes;
    public string? Subject { get; init; } = TestTokens.DefaultSubject;
    public string Issuer { get; init; } = TestTokens.Issuer;
    public string Audience { get; init; } = TestTokens.Audience;
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromHours(1);
    public bool UseUntrustedKey { get; init; }
    public string ScopeClaim { get; init; } = "scope";
}
