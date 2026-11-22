using System.Text.Json.Serialization;

namespace Aggregator.Api.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<SourceStatus>))]
public enum SourceStatus
{
    Success,
    Cached,
    Fallback,
    Failed,
}
