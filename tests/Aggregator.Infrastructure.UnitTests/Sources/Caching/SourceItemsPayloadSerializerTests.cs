using System.Buffers;
using System.Text;
using System.Text.Json;
using Aggregator.Application.Sources;
using Aggregator.Domain.Items;
using Aggregator.Infrastructure.Sources.Caching;

namespace Aggregator.Infrastructure.UnitTests.Sources.Caching;

public sealed class SourceItemsPayloadSerializerTests
{
    private readonly SourceItemsPayloadSerializer _serializer = new();

    [Fact]
    public void Deserialize_ShouldReturnTheSameItems_WhenThePayloadWasSerializedHere()
    {
        var entry = new CachedItems(
            [CachingHost.Item("n", new NewsDetails(Author: "Ada", Points: 1, Publisher: null))], CachingHost.StoredAt, "hacker-news");
        var buffer = new ArrayBufferWriter<byte>();

        _serializer.Serialize(entry, buffer);
        var read = _serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory));

        read.Items.ShouldBe(entry.Items);
        read.StoredAt.ShouldBe(entry.StoredAt);
        read.Provider.ShouldBe("hacker-news");
    }

    [Theory]
    [InlineData("""{"StoredAt":"2026-11-22T12:00:00+00:00","Provider":null}""")]
    [InlineData("""{"StoredAt":"2026-11-22T12:00:00+00:00","Provider":null,"Items":[{"Id":"r","Source":"s","Category":"c","Title":"t","Summary":null,"Url":null,"PublishedAt":"2026-11-22T12:00:00+00:00","Relevance":2,"Details":{"kind":"repository","Stars":1,"Forks":0,"Language":null}}]}""")]
    [InlineData("""{"StoredAt":"2026-11-22T12:00:00+00:00","Provider":null,"Items":[{"Id":"r","Source":"s","Category":"c","Title":"t","Summary":null,"Url":null,"PublishedAt":"2026-11-22T12:00:00+00:00","Relevance":0.5,"Details":null}]}""")]
    public void Deserialize_ShouldThrowJsonException_WhenTheEntryCannotBecomeItems(string json) =>
        Should.Throw<JsonException>(() => _serializer.Deserialize(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(json))));
}
