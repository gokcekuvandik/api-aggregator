using Aggregator.Api.Contracts;
using Aggregator.Application.Aggregation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Aggregator.Api.Endpoints.Aggregation;

public sealed class GetAggregationEndpoint : IEndpoint
{
    private const string Description =
        "Fans out to every source and merges the results. Returns 200 when at least one source " +
        "produced data, including cached or fallback data, so a 200 can still list failed sources: " +
        "read sources[] to detect degradation. Returns 503 when every source failed.";

    public void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/aggregation", HandleAsync)
            .WithName("GetAggregation")
            .WithSummary("Aggregated items from all sources")
            .WithDescription(Description)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    private static async Task<Results<Ok<AggregationResponse>, ProblemHttpResult>> HandleAsync(
        [AsParameters] AggregationQuery query,
        AggregationService service,
        CancellationToken cancellationToken)
    {
        var result = await service.AggregateAsync(query, cancellationToken);
        IReadOnlyList<SourceStatusResponse> sources = [.. result.Sources.Select(SourceStatusResponse.From)];

        if (!result.HasData)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "No source returned data",
                detail: "Every source failed and no fallback data was available. See sources for details.",
                extensions: new Dictionary<string, object?> { ["sources"] = sources });
        }

        return TypedResults.Ok(AggregationResponse.From(result, sources));
    }
}
