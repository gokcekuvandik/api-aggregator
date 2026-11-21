using System.Net;
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
    public Task<SourceResult> FetchAsync<TPayload>(
        string sourceName,
        HttpRequestMessage request,
        JsonTypeInfo<TPayload> payloadType,
        Func<TPayload, IReadOnlyList<AggregatedItem>> toItems,
        CancellationToken cancellationToken) =>
        FetchAsync(sourceName, request, payloadType, toItems, noItemsStatus: null, cancellationToken);

    /// <summary>
    /// Like the overload above, but an answer of <paramref name="noItemsStatus"/> means nothing matched and yields no items.
    /// Use it only where the upstream signals "no match" with that status, such as an unknown city name.
    /// </summary>
    public async Task<SourceResult> FetchAsync<TPayload>(
        string sourceName,
        HttpRequestMessage request,
        JsonTypeInfo<TPayload> payloadType,
        Func<TPayload, IReadOnlyList<AggregatedItem>> toItems,
        HttpStatusCode? noItemsStatus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(toItems);

        var (payload, outcome) = await ReadCoreAsync(sourceName, request, payloadType, noItemsStatus, cancellationToken);
        return outcome ?? new SourceSuccess(toItems(payload!));
    }

    /// <summary>
    /// Like <see cref="FetchAsync{TPayload}(string, HttpRequestMessage, JsonTypeInfo{TPayload}, Func{TPayload, IReadOnlyList{AggregatedItem}}, CancellationToken)"/>
    /// but hands back the payload, for a request whose answer feeds the next one.
    /// Exactly one of the two values is set. Takes ownership of <paramref name="request"/> and disposes it.
    /// </summary>
    public async Task<(TPayload? Payload, SourceFailure? Failure)> ReadAsync<TPayload>(
        string sourceName,
        HttpRequestMessage request,
        JsonTypeInfo<TPayload> payloadType,
        CancellationToken cancellationToken)
    {
        var (payload, outcome) = await ReadCoreAsync(sourceName, request, payloadType, noItemsStatus: null, cancellationToken);
        return (payload, outcome as SourceFailure);
    }

    /// <summary>Either the payload, or the result to return instead of mapping it: a failure, or no items.</summary>
    private async Task<(TPayload? Payload, SourceResult? Outcome)> ReadCoreAsync<TPayload>(
        string sourceName,
        HttpRequestMessage request,
        JsonTypeInfo<TPayload> payloadType,
        HttpStatusCode? noItemsStatus,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        (bool NoItems, TPayload? Payload) sent;
        using (request)
        {
            try
            {
                sent = await SendAsync(sourceName, request, payloadType, noItemsStatus, cancellationToken);
            }
            catch (Exception exception) when (Describe(exception, sourceName, cancellationToken) is { } error)
            {
                LogSourceFailed(sourceName, error, exception);
                return (default, new SourceFailure(error));
            }
        }

        if (sent.NoItems)
        {
            return (default, new SourceSuccess([]));
        }

        if (sent.Payload is null)
        {
            var error = Unreadable(sourceName);
            LogSourceFailed(sourceName, error, exception: null);
            return (default, new SourceFailure(error));
        }

        return (sent.Payload, null);
    }

    private async Task<(bool NoItems, TPayload? Payload)> SendAsync<TPayload>(
        string sourceName, HttpRequestMessage request, JsonTypeInfo<TPayload> payloadType, HttpStatusCode? noItemsStatus, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(sourceName);
        // The body is buffered inside the pipeline, so the resilience timeouts and the client's size cap cover it too.
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        if (response.StatusCode == noItemsStatus)
        {
            return (true, default);
        }

        response.EnsureSuccessStatusCode();
        return (false, await response.Content.ReadFromJsonAsync(payloadType, cancellationToken));
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
