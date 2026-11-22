using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Aggregator.Api.Security;

/// <summary>Documents the bearer scheme once, and on each protected operation its scope, so Scalar's Authorize button works.</summary>
/// <remarks>The route group declares the 401, 403 and 429 ProblemDetails answers; this adds their descriptions and the 429 Retry-After header.</remarks>
internal sealed class BearerSecurityTransformer : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public const string SchemeName = "Bearer";

    private const string TooManyRequests = "429";

    private static readonly (string Status, string Description)[] SecurityResponses =
    [
        ("401", "The bearer token is missing or invalid."),
        ("403", "The token lacks the scope this operation requires."),
        (TooManyRequests, "Too many requests from this caller; retry after the Retry-After header."),
    ];

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.AddComponent<IOpenApiSecurityScheme>(SchemeName, new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = $"A JWT carrying the {string.Join(" or ", ApiScopes.All)} scope.",
        });
        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);
        List<string> scopes = [.. context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAuthorizeData>()
            .Select(data => data.Policy)
            .OfType<string>()
            .Where(ApiScopes.All.Contains)
            .Distinct(StringComparer.Ordinal)];
        if (scopes.Count == 0)
        {
            return Task.CompletedTask;
        }

        operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = scopes }];
        foreach (var (status, description) in SecurityResponses)
        {
            if (operation.Responses?.GetValueOrDefault(status) is not OpenApiResponse response)
            {
                continue;
            }

            response.Description = description;
            if (status == TooManyRequests)
            {
                response.Headers ??= new Dictionary<string, IOpenApiHeader>();
                response.Headers["Retry-After"] = new OpenApiHeader
                {
                    Description = "Seconds to wait before the next request.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
                };
            }
        }

        return Task.CompletedTask;
    }
}
