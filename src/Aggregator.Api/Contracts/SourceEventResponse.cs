using Aggregator.Application.Aggregation;

namespace Aggregator.Api.Contracts;

/// <summary>Data of a <c>source</c> event from <c>GET /api/v1/aggregation/stream</c>: one source's status and its filtered, sorted items.</summary>
public sealed record SourceEventResponse(SourceStatusResponse Source, IReadOnlyList<ItemResponse> Items)
{
    public static SourceEventResponse From(SourceUpdateResult update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return new(SourceStatusResponse.From(update.Report), [.. update.Items.Select(ItemResponse.From)]);
    }
}
