using System.Text.Json.Serialization;

namespace Aggregator.Infrastructure.Sources.Caching;

[JsonSerializable(typeof(SourceItemsPayload))]
internal sealed partial class SourceCacheJsonContext : JsonSerializerContext;
