namespace Aggregator.Api.Endpoints;

/// <summary>One feature's routes; implementations are discovered and mapped at startup.</summary>
public interface IEndpoint
{
    void Map(IEndpointRouteBuilder group);
}
