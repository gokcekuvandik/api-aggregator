namespace Aggregator.Api.Endpoints;

internal static class EndpointRegistration
{
    /// <summary>Maps every <see cref="IEndpoint"/> in this assembly under <c>/api/v1</c>.</summary>
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.NewVersionedApi("Aggregator")
            .MapGroup("/api/v{version:apiVersion}")
            .HasApiVersion(1, 0);

        foreach (var endpoint in DiscoverEndpoints())
        {
            endpoint.Map(v1);
        }

        return app;
    }

    private static IEnumerable<IEndpoint> DiscoverEndpoints() =>
        typeof(IEndpoint).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(IEndpoint)))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => (IEndpoint)Activator.CreateInstance(type)!);
}
