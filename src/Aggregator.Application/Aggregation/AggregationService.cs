using System.Diagnostics;
using Aggregator.Application.Aggregation.Filtering;
using Aggregator.Application.Aggregation.Sorting;
using Aggregator.Application.Paging;
using Aggregator.Application.Sources;

namespace Aggregator.Application.Aggregation;

/// <summary>
/// Queries the selected sources concurrently, then filters, sorts and pages their merged items.
/// Call <see cref="Validate"/> first: a query naming unknown sources or categories is a client error.
/// </summary>
public sealed class AggregationService(DataSourceRegistry registry)
{
    public IReadOnlyDictionary<string, string[]> Validate(AggregationQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var unknownSources = query.Sources.Where(name => !registry.TryGet(name, out _)).ToList();
        if (unknownSources.Count > 0)
        {
            var known = string.Join(", ", registry.All.Select(source => source.Name));
            errors[nameof(AggregationQuery.Sources)] = [.. unknownSources.Select(name => $"Unknown source '{name}'. Known sources: {known}.")];
        }

        if (query.Category is { } category)
        {
            if (!registry.All.Any(source => Serves(source, category)))
            {
                var known = string.Join(", ", registry.All.SelectMany(source => source.Categories).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal));
                errors[nameof(AggregationQuery.Category)] = [$"Unknown category '{category}'. Known categories: {known}."];
            }
            else if (unknownSources.Count == 0 && Select(query).Count == 0)
            {
                errors[nameof(AggregationQuery.Category)] = [$"None of the selected sources serves category '{category}'."];
            }
        }

        return errors;
    }

    public async Task<AggregationResult> AggregateAsync(AggregationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Validate(query).Count > 0)
        {
            throw new ArgumentException("The query names unknown sources or categories; validate it first.", nameof(query));
        }

        var reports = await Task.WhenAll(Select(query).Select(source => FetchAsync(source, query, cancellationToken)));
        var filtered = ItemFilters.Apply(reports.SelectMany(report => report.Result.Items), ItemFilters.For(query));
        var sorted = ItemOrdering.Sort(filtered, query.SortBy, query.SortOrder);

        return new AggregationResult(PageRequest.Apply(sorted, query.Page, query.PageSize), reports);
    }

    private List<IDataSource> Select(AggregationQuery query) =>
        [.. registry.All.Where(source =>
            (query.Sources.Count == 0 || query.Sources.Contains(source.Name, StringComparer.OrdinalIgnoreCase))
            && (query.Category is null || Serves(source, query.Category)))];

    private static bool Serves(IDataSource source, string category) =>
        source.Categories.Contains(category, StringComparer.OrdinalIgnoreCase);

    private static async Task<SourceReport> FetchAsync(IDataSource source, AggregationQuery query, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await source.FetchAsync(query, cancellationToken);
        return new SourceReport(source.Name, result, Stopwatch.GetElapsedTime(started));
    }
}
