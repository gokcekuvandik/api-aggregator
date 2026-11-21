using Aggregator.Api.Contracts;
using Aggregator.Application.Aggregation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Aggregator.Api.Endpoints.Aggregation;

public sealed class GetAggregationEndpoint : IEndpoint
{
    private const string Description =
        "Fans out to the selected sources, then filters, sorts and pages the merged items. " +
        "q and city are passed to the sources that support them; category and the inclusive from/to range " +
        "are applied to every item. Relevance is a 0–1 heuristic normalised per source. " +
        "Returns 200 when at least one source produced data, including cached or fallback data, so a 200 can " +
        "still list failed sources: read sources[] to detect degradation. Each source's answer is cached per search, " +
        "so Cached and Fallback entries give the data's age in ageSeconds. Returns 400 for invalid parameters, " +
        "including unknown sources or categories, and 503 when every source failed.";

    public void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/aggregation", HandleAsync)
            .WithName("GetAggregation")
            .WithSummary("Aggregated items from all sources")
            .WithDescription(Description)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    private static async Task<Results<Ok<AggregationResponse>, ValidationProblem, ProblemHttpResult>> HandleAsync(
        [AsParameters] AggregationRequest request,
        AggregationService service,
        CancellationToken cancellationToken)
    {
        var query = request.ToQuery();
        var errors = service.Validate(query);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

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
