using System.Security.Cryptography;
using System.Text;
using Aggregator.Application.Aggregation;

namespace Aggregator.Application.Sources;

/// <summary>
/// Builds <c>source:{name}:v1:{search}</c> from only the search fields a source uses, in a fixed order and lower-cased; a long search is hashed.
/// Category, sorting and paging are applied after the fetch, so they are left out.
/// </summary>
public static class SourceCacheKey
{
    /// <summary>Longer keys have their search part hashed, which keeps every key well under the cache's 1024-character limit.</summary>
    public const int MaxLength = 256;

    public static string For(string sourceName, SearchFields fields, AggregationQuery query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(query);

        string?[] parts =
        [
            fields.HasFlag(SearchFields.Text) ? $"q={Escape(query.Text)}" : null,
            fields.HasFlag(SearchFields.City) ? $"city={Escape(query.City)}" : null,
            fields.HasFlag(SearchFields.From) ? FormattableString.Invariant($"from={query.From:O}") : null,
            fields.HasFlag(SearchFields.To) ? FormattableString.Invariant($"to={query.To:O}") : null,
        ];

        var prefix = $"source:{sourceName}:v1:".ToLowerInvariant();
        var search = string.Join('|', parts.OfType<string>()).ToLowerInvariant();
        return prefix.Length + search.Length <= MaxLength
            ? prefix + search
            : prefix + "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(search)));
    }

    // Escaping keeps a separator typed into the search text from making two searches share a key.
    private static string Escape(string? value) => value is null ? string.Empty : Uri.EscapeDataString(value);
}
