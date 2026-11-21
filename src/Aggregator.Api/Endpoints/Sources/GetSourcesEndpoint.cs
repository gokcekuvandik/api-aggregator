using Aggregator.Api.Contracts;
using Aggregator.Application.Sources;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Aggregator.Api.Endpoints.Sources;

public sealed class GetSourcesEndpoint : IEndpoint
{
    public void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/sources", Handle)
            .WithName("GetSources")
            .WithSummary("The registered data sources and their categories");

    private static Ok<SourcesResponse> Handle(IEnumerable<IDataSource> sources) =>
        TypedResults.Ok(new SourcesResponse([.. sources.Select(s => new SourceInfoResponse(s.Name, s.Categories))]));
}
