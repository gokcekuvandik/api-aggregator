using System.Net;
using Aggregator.Application.Statistics;
using Aggregator.Infrastructure.Sources;

namespace Aggregator.Infrastructure.Statistics;

/// <summary>
/// Measures each request outside the resilience pipeline, so the duration is what the caller waited, retries and body included.
/// It counts the attempts that <see cref="AttemptCountingHandler"/> saw below the pipeline, decides the outcome, and records it once through <see cref="ISourceTelemetry"/>.
/// </summary>
internal sealed class StatisticsHandler(ISourceTelemetry telemetry, UpstreamApi api, TimeProvider time) : DelegatingHandler
{
    public UpstreamApi Api => api;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var attempts = AttemptCountingHandler.Attach(request);
        var started = time.GetTimestamp();
        var outcome = RequestOutcome.Failure;
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
            await response.Content.LoadIntoBufferAsync(DataSourceServiceCollectionExtensions.MaxResponseBytes, cancellationToken);
            outcome = response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified ? RequestOutcome.Success : RequestOutcome.Failure;
            return response;
        }
        catch (Exception exception)
        {
            // The token also fires on HttpClient.Timeout, so a body stalled past it reads as cancelled; the pipeline's own timeouts are failures.
            outcome = exception is OperationCanceledException && cancellationToken.IsCancellationRequested ? RequestOutcome.Cancelled
                : attempts.Count == 0 ? RequestOutcome.Rejected
                : RequestOutcome.Failure;
            response?.Dispose();
            throw;
        }
        finally
        {
            telemetry.RecordRequest(api, time.GetElapsedTime(started), outcome, attempts.Count);
        }
    }
}
