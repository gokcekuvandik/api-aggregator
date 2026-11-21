using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Aggregator.Infrastructure.Sources.Http;

/// <summary>Logs the upstream rate-limit headers so a source running out of quota is visible before it fails.</summary>
internal sealed partial class RateLimitLoggingHandler(ILogger<RateLimitLoggingHandler> logger, string sourceName) : DelegatingHandler
{
    private const int LowRemainingThreshold = 5;

    private const string RemainingHeader = "X-RateLimit-Remaining";
    private const string ResetHeader = "X-RateLimit-Reset";
    private const string Unknown = "unknown";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            LogRateLimited(sourceName, RetryAfter(response));
        }
        else if (int.TryParse(FirstValue(response, RemainingHeader), NumberStyles.None, CultureInfo.InvariantCulture, out var remaining))
        {
            var reset = FirstValue(response, ResetHeader) ?? Unknown;
            if (remaining <= LowRemainingThreshold)
            {
                LogQuotaLow(sourceName, remaining, reset);
            }
            else
            {
                LogQuota(sourceName, remaining, reset);
            }
        }

        return response;
    }

    private static string? FirstValue(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out var values) ? values.FirstOrDefault() : null;

    private static string RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta.ToString("c", CultureInfo.InvariantCulture),
        { Date: { } date } => date.ToString("O", CultureInfo.InvariantCulture),
        _ => Unknown,
    };

    [LoggerMessage(Level = LogLevel.Debug, Message = "Source {Source} has {Remaining} requests left; the quota resets at {Reset}")]
    private partial void LogQuota(string source, int remaining, string reset);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {Source} has only {Remaining} requests left; the quota resets at {Reset}")]
    private partial void LogQuotaLow(string source, int remaining, string reset);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {Source} is rate limiting requests; retry after {RetryAfter}")]
    private partial void LogRateLimited(string source, string retryAfter);
}
