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

public sealed record ItemResponse(
    string Id,
    string Source,
    string Category,
    string Title,
    string? Summary,
    Uri? Url,
    DateTimeOffset PublishedAt)
{
    public static ItemResponse From(Domain.Items.AggregatedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new(item.Id, item.Source, item.Category, item.Title, item.Summary, item.Url, item.PublishedAt);
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
        var durationMs = report.Duration.TotalMilliseconds;
        return report.Result switch
        {
            SourceSuccess => new(report.Name, SourceStatus.Success, durationMs, null, null),
            SourceCached => new(report.Name, SourceStatus.Cached, durationMs, null, null),
            SourceFallback fallback => new(report.Name, SourceStatus.Fallback, durationMs, null, fallback.Age.TotalSeconds),
            SourceFailure failure => new(report.Name, SourceStatus.Failed, durationMs, failure.Error, null),
            _ => throw new UnreachableException($"Unknown source result {report.Result.GetType().Name}."),
        };
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
