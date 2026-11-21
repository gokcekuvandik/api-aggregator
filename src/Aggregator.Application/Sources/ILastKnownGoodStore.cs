using System.Globalization;
using Aggregator.Application.Aggregation;
using Aggregator.Domain.Items;

namespace Aggregator.Application.Sources;

/// <summary>The last items a source answered for one search, and when they were stored.</summary>
public sealed record LastKnownGood(IReadOnlyList<AggregatedItem> Items, DateTimeOffset StoredAt);

/// <summary>Last-known-good items per source and search, read only when every provider of a source has failed.</summary>
public interface ILastKnownGoodStore
{
    LastKnownGood? Find(string key);

    void Save(string key, LastKnownGood entry);
}

/// <summary>
/// Builds <c>source:{name}:v1:{search}</c> from the fields a source uses to build its request, lower-cased.
/// Category, sorting and paging are applied after the fetch, so they are left out.
/// </summary>
public static class LastKnownGoodKey
{
    public static string For(string sourceName, AggregationQuery query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(query);

        return string.Create(CultureInfo.InvariantCulture,
            $"source:{sourceName}:v1:q={Escape(query.Text)}|city={Escape(query.City)}|from={query.From:O}|to={query.To:O}").ToLowerInvariant();
    }

    // Escaping keeps a separator typed into the search text from making two searches share a key.
    private static string Escape(string? value) => value is null ? string.Empty : Uri.EscapeDataString(value);
}
