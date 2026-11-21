using System.Buffers;
using System.Text.Json;
using Aggregator.Application.Sources;
using Microsoft.Extensions.Caching.Hybrid;

namespace Aggregator.Infrastructure.Sources.Caching;

/// <summary>
/// Writes cached source items as source-generated JSON through <see cref="SourceItemsPayload"/>.
/// An entry that cannot become domain items is rejected here, so the cache treats it as unreadable rather than failing the request.
/// </summary>
internal sealed class SourceItemsPayloadSerializer : IHybridCacheSerializer<CachedItems>
{
    public CachedItems Deserialize(ReadOnlySequence<byte> source)
    {
        var reader = new Utf8JsonReader(source);
        var payload = JsonSerializer.Deserialize(ref reader, SourceCacheJsonContext.Default.SourceItemsPayload);
        if (payload?.Items is null || payload.Items.Any(item => item?.Details is null))
        {
            throw new JsonException("A cached source entry has no items or an item without details.");
        }

        try
        {
            return payload.ToCachedItems();
        }
        catch (ArgumentException exception)
        {
            throw new JsonException("A cached source entry holds an invalid item.", exception);
        }
    }

    public void Serialize(CachedItems value, IBufferWriter<byte> target)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var writer = new Utf8JsonWriter(target);
        JsonSerializer.Serialize(writer, SourceItemsPayload.From(value), SourceCacheJsonContext.Default.SourceItemsPayload);
    }
}
