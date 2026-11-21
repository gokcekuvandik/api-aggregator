using System.Text.Json;

namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>Reads the parts of an aggregation body that tests assert on.</summary>
internal static class AggregationBody
{
    public static JsonElement Source(JsonElement body, string name) =>
        body.GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);

    public static Dictionary<string, string?> Statuses(JsonElement body) =>
        body.GetProperty("sources").EnumerateArray().ToDictionary(s => s.GetProperty("name").GetString()!, s => s.GetProperty("status").GetString());

    public static List<string?> Ids(JsonElement body) =>
        [.. body.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString())];
}
