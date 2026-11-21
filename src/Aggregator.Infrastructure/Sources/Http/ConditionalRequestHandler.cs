using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregator.Infrastructure.Sources.Http;

/// <summary>The last successful body for a URL and the ETag that identifies it.</summary>
internal sealed record StoredResponse(EntityTagHeaderValue ETag, byte[] Body, string? MediaType);

/// <summary>Stored responses by absolute URL, bounded by evicting the least recently used URL.</summary>
internal sealed class ConditionalResponseStore(int capacity = ConditionalResponseStore.DefaultCapacity)
{
    public const int DefaultCapacity = 256;

    private readonly LruStore<StoredResponse> _entries = new(capacity);

    public bool TryGet(string key, [NotNullWhen(true)] out StoredResponse? response) => _entries.TryGet(key, out response);

    public void Set(string key, StoredResponse response) => _entries.Set(key, response);
}

/// <summary>
/// Sends <c>If-None-Match</c> with the stored ETag and turns a 304 back into a 200 with the stored body.
/// Upstreams such as GitHub do not count a 304 against the rate limit.
/// </summary>
internal sealed class ConditionalRequestHandler(ConditionalResponseStore store) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get || request.RequestUri is not { } uri)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var key = uri.AbsoluteUri;
        store.TryGet(key, out var stored);
        if (stored is not null && request.Headers.IfNoneMatch.Count == 0)
        {
            request.Headers.IfNoneMatch.Add(stored.ETag);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified && stored is not null)
        {
            using (response)
            {
                return Replay(request, response, stored);
            }
        }

        if (response.IsSuccessStatusCode && response.Headers.ETag is { } etag)
        {
            try
            {
                await response.Content.LoadIntoBufferAsync(DataSourceServiceCollectionExtensions.MaxResponseBytes, cancellationToken);
            }
            catch
            {
                response.Dispose();
                throw;
            }

            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            store.Set(key, new StoredResponse(etag, body, response.Content.Headers.ContentType?.MediaType));
        }

        return response;
    }

    private static HttpResponseMessage Replay(HttpRequestMessage request, HttpResponseMessage notModified, StoredResponse stored)
    {
        var content = new ByteArrayContent(stored.Body);
        if (stored.MediaType is not null)
        {
            content.Headers.ContentType = new MediaTypeHeaderValue(stored.MediaType);
        }

        var replay = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = content };
        foreach (var (name, values) in notModified.Headers)
        {
            replay.Headers.TryAddWithoutValidation(name, values);
        }

        return replay;
    }
}

internal static class ConditionalRequestExtensions
{
    /// <summary>Adds ETag revalidation to one source's client.</summary>
    public static IHttpClientBuilder AddConditionalRequests(this IHttpClientBuilder builder)
    {
        builder.Services.TryAddSingleton<ConditionalResponseStore>();
        builder.Services.TryAddTransient<ConditionalRequestHandler>();
        return builder.AddHttpMessageHandler<ConditionalRequestHandler>();
    }
}
