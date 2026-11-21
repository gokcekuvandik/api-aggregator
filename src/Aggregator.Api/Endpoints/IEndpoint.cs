namespace Aggregator.Api.Endpoints;

/// <summary>
/// One feature's routes. Implementations are discovered at startup and mapped onto the
/// versioned <c>/api/v{version}</c> group, so adding an endpoint needs no wiring (ADR-0002).
/// </summary>
public interface IEndpoint
{
    void Map(IEndpointRouteBuilder group);
}
