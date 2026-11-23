using Aggregator.Api.Http;
using Aggregator.Api.Security;

namespace Aggregator.Api.Endpoints;

internal static class EndpointRegistration
{
    /// <summary>Maps every <see cref="IEndpoint"/> in this assembly under <c>/api/v1</c>.</summary>
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.NewVersionedApi("Aggregator")
            .MapGroup("/api/v{version:apiVersion}")
            .HasApiVersion(1, 0)
            .RequireAuthorization()
            .RequireRateLimiting(CallerRateLimiting.PolicyName)
            // The logging interceptor wraps validation, so a rejected request is logged with its invalid fields.
            .AddEndpointFilter<EndpointLoggingFilter>()
            .AddEndpointFilter<ValidationInterceptor>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        foreach (var endpoint in DiscoverEndpoints())
        {
            endpoint.Map(v1);
        }

        return app;
    }

    /// <summary>Every concrete <see cref="IEndpoint"/> in this assembly, in a stable order.</summary>
    internal static IReadOnlyList<IEndpoint> DiscoverEndpoints() =>
        [.. typeof(IEndpoint).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(IEndpoint)))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .Select(type => (IEndpoint)Activator.CreateInstance(type)!)];
}
