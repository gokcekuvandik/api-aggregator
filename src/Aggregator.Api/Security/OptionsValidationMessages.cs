namespace Aggregator.Api.Security;

/// <summary>Startup failures for security settings, each naming the configuration key to fix.</summary>
internal static class OptionsValidationMessages
{
    public const string InvalidRateLimiting =
        $"{RateLimitingOptions.SectionName}: TokenLimit and TokensPerPeriod must be at least 1, and ReplenishmentPeriod positive and at most an hour.";

    public const string UnusableBearerScheme =
        "Authentication:Schemes:Bearer needs ValidAudiences, and either an Authority or a ValidIssuer with SigningKeys. " +
        "For development run: dotnet user-jwts create --project src/Aggregator.Api --scope aggregation:read --scope stats:read";
}
