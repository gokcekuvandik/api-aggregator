using System.Security.Claims;

namespace Aggregator.Api.Security;

/// <summary>The OAuth scopes the API checks; each one is also the name of the policy that requires it.</summary>
internal static class ApiScopes
{
    public const string AggregationRead = "aggregation:read";
    public const string StatsRead = "stats:read";

    private static readonly string[] ClaimTypes = ["scope", "scp"];

    public static IReadOnlyList<string> All { get; } = [AggregationRead, StatsRead];

    /// <summary>True when <paramref name="user"/> holds <paramref name="scope"/>, sent as separate claims or as one space-separated value.</summary>
    public static bool IsGranted(ClaimsPrincipal user, string scope)
    {
        foreach (var claim in user.Claims)
        {
            if (ClaimTypes.Contains(claim.Type, StringComparer.Ordinal)
                && (claim.Value == scope || claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(scope, StringComparer.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }
}
