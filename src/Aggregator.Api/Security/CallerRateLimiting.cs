using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Aggregator.Api.Security;

/// <summary>A token bucket per caller, keyed by the JWT subject or, for a token without one, the client address. It is per instance (ADR-0013).</summary>
internal static class CallerRateLimiting
{
    public const string PolicyName = "per-caller";

    public static IServiceCollection AddCallerRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<RateLimitingOptions>()
            .BindConfiguration(RateLimitingOptions.SectionName)
            .Validate(
                options => options.IsValid,
                OptionsValidationMessages.InvalidRateLimiting)
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = WriteRetryAfter;
            limiter.AddPolicy(PolicyName, context =>
            {
                var settings = context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return RateLimitPartition.GetTokenBucketLimiter(PartitionKey(context), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = settings.TokenLimit,
                    TokensPerPeriod = settings.TokensPerPeriod,
                    ReplenishmentPeriod = settings.ReplenishmentPeriod,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });
        });

        return services;
    }

    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) is { Length: > 0 } subject
            ? $"sub:{subject}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    /// <summary>The body is left to the status code pages, which write ProblemDetails.</summary>
    private static ValueTask WriteRetryAfter(OnRejectedContext context, CancellationToken cancellationToken)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        return ValueTask.CompletedTask;
    }
}
