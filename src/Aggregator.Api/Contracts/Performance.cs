using System.Text.Json.Serialization;

namespace Aggregator.Api.Contracts;

/// <summary>The bucket an API's average duration falls into.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Performance>))]
public enum Performance
{
    Fast,
    Average,
    Slow,
}
