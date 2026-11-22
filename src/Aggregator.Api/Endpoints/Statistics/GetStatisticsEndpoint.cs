using Aggregator.Api.Contracts;
using Aggregator.Api.Security;
using Aggregator.Application.Statistics;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Aggregator.Api.Endpoints.Statistics;

public sealed class GetStatisticsEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/statistics", Handle)
            .WithName("GetStatistics")
            .RequireAuthorization(ApiScopes.StatsRead)
            .WithSummary("Request statistics per source");

    private static Ok<StatisticsResponse> Handle(IStatisticsReader statistics) =>
        TypedResults.Ok(new StatisticsResponse([.. statistics.GetSnapshots().Select(SourceStatisticsResponse.From)]));
}
