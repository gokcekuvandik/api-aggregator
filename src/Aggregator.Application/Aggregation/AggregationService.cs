using System.Diagnostics;
using System.Runtime.CompilerServices;
using Aggregator.Application.Aggregation.Filtering;
using Aggregator.Application.Aggregation.Sorting;
using Aggregator.Application.Paging;
using Aggregator.Application.Sources;
using Aggregator.Application.Telemetry;
using Aggregator.Domain.Items;

namespace Aggregator.Application.Aggregation;

/// <summary>
/// Queries the selected sources concurrently, then filters, sorts and pages their merged items.
/// Call <see cref="Validate"/> first: a query naming unknown sources or categories is a client error.
/// Each aggregation is traced as an <c>aggregate</c> span with one child per source, and its duration is recorded by outcome.
/// </summary>
public sealed class AggregationService(DataSourceRegistry registry, AggregationTelemetry telemetry)
{
    private const string PageMode = "page";
    private const string StreamMode = "stream";

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
        EnsureValid(query);

        var sources = Select(query);
        using var run = telemetry.StartRun(PageMode, sources.Count);
        var reports = await Task.WhenAll(sources.Select(source => FetchAsync(source, query, run, cancellationToken)));
        var sorted = FilterAndSort(reports.SelectMany(report => report.Result.Items), query);
        run.Completed(reports);

        return new AggregationResult(PageRequest.Apply(sorted, query.Page, query.PageSize), reports);
    }

    /// <summary>
    /// Yields each selected source as soon as it completes, fastest first, with its items filtered and sorted but not paged.
    /// Throws like <see cref="AggregateAsync"/> for a query that fails <see cref="Validate"/>.
    /// </summary>
    public async IAsyncEnumerable<SourceUpdate> StreamAsync(AggregationQuery query, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        EnsureValid(query);

        var sources = Select(query);
        using var run = telemetry.StartRun(StreamMode, sources.Count);

        // Fetches still running when the consumer stops early, or a fetch throws, are cancelled rather than left behind.
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var fetches = sources.Select(source => FetchAsync(source, query, run, pending.Token));
            var reports = new List<SourceReport>(sources.Count);
            await foreach (var completed in Task.WhenEach(fetches).WithCancellation(cancellationToken))
            {
                var report = await completed;
                reports.Add(report);
                yield return new SourceUpdate(report, FilterAndSort(report.Result.Items, query));
            }

            run.Completed(reports);
        }
        finally
        {
            await pending.CancelAsync();
        }
    }

    private void EnsureValid(AggregationQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (Validate(query).Count > 0)
        {
            throw new ArgumentException("The query names unknown sources or categories; validate it first.", nameof(query));
        }
    }

    private static IReadOnlyList<AggregatedItem> FilterAndSort(IEnumerable<AggregatedItem> items, AggregationQuery query) =>
        ItemOrdering.Sort(ItemFilters.Apply(items, ItemFilters.For(query)), query.SortBy, query.SortOrder);

    private List<IDataSource> Select(AggregationQuery query) =>
        [.. registry.All.Where(source =>
            (query.Sources.Count == 0 || query.Sources.Contains(source.Name, StringComparer.OrdinalIgnoreCase))
            && (query.Category is null || Serves(source, query.Category)))];

    private static bool Serves(IDataSource source, string category) =>
        source.Categories.Contains(category, StringComparer.OrdinalIgnoreCase);

    private static async Task<SourceReport> FetchAsync(IDataSource source, AggregationQuery query, AggregationRun run, CancellationToken cancellationToken)
    {
        using var span = run.StartSource(source.Name);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await source.FetchAsync(query, cancellationToken);
            span.Answered(result);
            return new SourceReport(source.Name, result, Stopwatch.GetElapsedTime(started));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            span.Cancelled();
            throw;
        }
        catch (Exception exception)
        {
            span.Threw(exception);
            throw;
        }
    }
}
