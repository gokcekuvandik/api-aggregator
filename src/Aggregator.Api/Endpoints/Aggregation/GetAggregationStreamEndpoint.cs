using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Aggregator.Api.Contracts;
using Aggregator.Api.Security;
using Aggregator.Application.Aggregation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Aggregator.Api.Endpoints.Aggregation;

public sealed class GetAggregationStreamEndpoint : IEndpoint
{
    private const string SourceEvent = "source";
    private const string DoneEvent = "done";

    private const string Description =
        "Streams server-sent events: one 'source' event per selected source as soon as it completes, fastest first, " +
        "with that source's filtered and sorted items (not paged), then one 'done' event. " +
        "Returns 400 for invalid parameters before the stream starts; once streaming, hasData in 'done' takes the place of the 503. " +
        "A stream that ends without 'done' was cut short by an error and is incomplete.";

    public void Map(IEndpointRouteBuilder group) =>
        group.MapGet("/aggregation/stream", Handle)
            .WithName("GetAggregationStream")
            .RequireAuthorization(ApiScopes.AggregationRead)
            .WithSummary("Aggregated items streamed per source")
            .WithDescription(Description)
            .ProducesValidationProblem();

    private static Results<ServerSentEventsResult<object>, ValidationProblem> Handle(
        [AsParameters] AggregationRequest request,
        AggregationService service,
        CancellationToken cancellationToken)
    {
        var query = request.ToQuery();
        var errors = service.Validate(query);
        return errors.Count > 0
            ? TypedResults.ValidationProblem(errors)
            : TypedResults.ServerSentEvents(EventsAsync(service, query, cancellationToken));
    }

    private static async IAsyncEnumerable<SseItem<object>> EventsAsync(
        AggregationService service, AggregationQuery query, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var count = 0;
        var hasData = false;
        await foreach (var update in service.StreamAsync(query, cancellationToken))
        {
            count++;
            hasData |= update.Report.HasData;
            yield return new SseItem<object>(SourceEventResponse.From(update), SourceEvent);
        }

        yield return new SseItem<object>(new StreamCompletedResponse(count, hasData), DoneEvent);
    }
}
