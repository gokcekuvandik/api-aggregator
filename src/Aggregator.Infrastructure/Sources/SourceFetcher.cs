using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Aggregator.Application.Sources;
using Aggregator.Domain.Items;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Timeout;

namespace Aggregator.Infrastructure.Sources;

/// <summary>
/// Sends one request on the source's named client and maps the JSON payload to items.
/// Transport, timeout, circuit and payload failures become a safe <see cref="SourceFailure"/> and are logged with details.
/// Caller cancellation and mapping bugs still throw.
/// </summary>
internal sealed partial class SourceFetcher(IHttpClientFactory httpClientFactory, ILogger<SourceFetcher> logger)
{
    /// <summary>Takes ownership of <paramref name="request"/> and disposes it.</summary>
    public async Task<SourceResult> FetchAsync<TPayload>(
        string sourceName,
        HttpRequestMessage request,
        JsonTypeInfo<TPayload> payloadType,
        Func<TPayload, IReadOnlyList<AggregatedItem>> toItems,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(toItems);

        TPayload? payload;
        using (request)
        {
            try
            {
                payload = await ReadAsync(sourceName, request, payloadType, cancellationToken);
            }
            catch (Exception exception) when (Describe(exception, sourceName, cancellationToken) is { } error)
            {
                LogSourceFailed(sourceName, error, exception);
                return new SourceFailure(error);
            }
        }

        if (payload is null)
        {
            var error = Unreadable(sourceName);
            LogSourceFailed(sourceName, error, exception: null);
            return new SourceFailure(error);
        }

        return new SourceSuccess(toItems(payload));
    }

    private async Task<TPayload?> ReadAsync<TPayload>(
        string sourceName, HttpRequestMessage request, JsonTypeInfo<TPayload> payloadType, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(sourceName);
        // The body is buffered inside the pipeline, so the resilience timeouts and the client's size cap cover it too.
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(payloadType, cancellationToken);
    }

    private static string? Describe(Exception exception, string sourceName, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => null,
        HttpRequestException { StatusCode: { } status } => $"{sourceName} answered HTTP {(int)status}.",
        HttpRequestException or IOException => $"{sourceName} could not be reached.",
        TimeoutRejectedException or OperationCanceledException => $"{sourceName} timed out.",
        ExecutionRejectedException => $"{sourceName} is temporarily unavailable.",
        JsonException => Unreadable(sourceName),
        _ => null,
    };

    private static string Unreadable(string sourceName) => $"{sourceName} returned an unreadable response.";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Source {Source} failed: {Error}")]
    private partial void LogSourceFailed(string source, string error, Exception? exception);
}
