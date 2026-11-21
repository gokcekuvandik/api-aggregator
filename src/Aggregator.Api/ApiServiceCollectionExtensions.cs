using System.Text.Json.Serialization;
using Aggregator.Api.Http;
using Asp.Versioning;

namespace Aggregator.Api;

internal static class ApiServiceCollectionExtensions
{
    /// <summary>HTTP-contract services: ProblemDetails, validation, versioning, OpenAPI (ADR-0002).</summary>
    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        // RFC 9457 bodies everywhere. The default writer adds "traceId".
        services.AddProblemDetails(options => options.CustomizeProblemDetails = ValidationErrorKeys.CamelCase);

        // Validates [AsParameters] / body types before the handler runs; failures are 400 ProblemDetails.
        services.AddValidation();

        // Numbers are numbers: no "123"-as-string in bodies, and OpenAPI types them as plain integers.
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

        services.AddApiVersioning(options =>
            {
                options.DefaultApiVersion = new ApiVersion(1, 0);
                options.ReportApiVersions = true;
                options.ApiVersionReader = new UrlSegmentApiVersionReader();
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'V";
                options.SubstituteApiVersionInUrl = true;
            });

        services.AddOpenApi("v1");

        return services;
    }
}
