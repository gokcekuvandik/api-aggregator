using System.Collections.Frozen;
using Aggregator.Application.Aggregation.Sorting;

namespace Aggregator.Api.Contracts;

/// <summary>Wire names of <c>sortBy</c> and <c>sortOrder</c>, matched ignoring case; numbers are not accepted.</summary>
internal static class SortParameters
{
    public const string FieldNames = "relevance, date, source";
    public const string DirectionNames = "desc, asc";

    private static readonly FrozenDictionary<string, ItemSortField> Fields = new Dictionary<string, ItemSortField>
    {
        ["relevance"] = ItemSortField.Relevance,
        ["date"] = ItemSortField.Date,
        ["source"] = ItemSortField.Source,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, SortDirection> Directions = new Dictionary<string, SortDirection>
    {
        ["desc"] = SortDirection.Descending,
        ["asc"] = SortDirection.Ascending,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static bool TryParseField(string? value, out ItemSortField field) =>
        TryParse(value, Fields, ItemSortField.Relevance, out field);

    public static bool TryParseDirection(string? value, out SortDirection direction) =>
        TryParse(value, Directions, SortDirection.Descending, out direction);

    public static ItemSortField ParseField(string? value) =>
        TryParseField(value, out var field) ? field : throw new ArgumentException($"sortBy must be one of: {FieldNames}.", nameof(value));

    public static SortDirection ParseDirection(string? value) =>
        TryParseDirection(value, out var direction) ? direction : throw new ArgumentException($"sortOrder must be one of: {DirectionNames}.", nameof(value));

    private static bool TryParse<T>(string? value, FrozenDictionary<string, T> names, T fallback, out T result)
        where T : struct
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = fallback;
            return true;
        }

        return names.TryGetValue(value.Trim(), out result);
    }
}
