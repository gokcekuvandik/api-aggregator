using System.Diagnostics;
using System.Text.Json.Serialization;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;

namespace Aggregator.Api.Contracts;

/// <summary>Body of a 200 from <c>GET /api/v1/aggregation</c>.</summary>
public sealed record AggregationResponse(
    IReadOnlyList<ItemResponse> Items,
    IReadOnlyList<SourceStatusResponse> Sources,
    PagingResponse Paging)
{
    public static AggregationResponse From(AggregationResult result, IReadOnlyList<SourceStatusResponse> sources)
    {
        ArgumentNullException.ThrowIfNull(result);
        var page = result.Items;
        return new AggregationResponse(
            [.. page.Items.Select(ItemResponse.From)],
            sources,
            new PagingResponse(page.Page, page.PageSize, page.TotalCount));
    }
}

/// <summary>One item; <c>relevance</c> is a 0–1 heuristic normalised per source, rounded to four decimals.</summary>
public sealed record ItemResponse(
    string Id,
    string Source,
    string Category,
    string Title,
    string? Summary,
    Uri? Url,
    DateTimeOffset PublishedAt,
    double Relevance,
    ItemDetailsResponse Details)
{
    private const int RelevanceDecimals = 4;

    public static ItemResponse From(Domain.Items.AggregatedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new(
            item.Id,
            item.Source,
            item.Category,
            item.Title,
            item.Summary,
            item.Url,
            item.PublishedAt,
            Math.Round(item.Relevance.Value, RelevanceDecimals),
            ItemDetailsResponse.From(item.Details));
    }
}

public sealed record PagingResponse(int Page, int PageSize, int TotalCount);

/// <summary>How one source fared. A 200 can still contain <see cref="SourceStatus.Failed"/> entries.</summary>
public sealed record SourceStatusResponse(
    string Name,
    SourceStatus Status,
    double DurationMs,
    string? Error,
    double? AgeSeconds)
{
    public static SourceStatusResponse From(SourceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.Result switch
        {
            SourceSuccess => Status(SourceStatus.Success),
            SourceCached => Status(SourceStatus.Cached),
            SourceFallback fallback => Status(SourceStatus.Fallback, ageSeconds: fallback.Age.TotalSeconds),
            SourceFailure failure => Status(SourceStatus.Failed, error: failure.Error),
            _ => throw new UnreachableException($"Unknown source result {report.Result.GetType().Name}."),
        };

        SourceStatusResponse Status(SourceStatus status, string? error = null, double? ageSeconds = null) =>
            new(report.Name, status, report.Duration.TotalMilliseconds, error, ageSeconds);
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<SourceStatus>))]
public enum SourceStatus
{
    Success,
    Cached,
    Fallback,
    Failed,
}
