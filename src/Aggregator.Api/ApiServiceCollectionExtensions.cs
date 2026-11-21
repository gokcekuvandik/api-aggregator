using System.Text.Json.Serialization;
using Aggregator.Api.Http;
using Asp.Versioning;

namespace Aggregator.Api;

internal static class ApiServiceCollectionExtensions
{
    /// <summary>Registers ProblemDetails, validation, API versioning and OpenAPI.</summary>
    public static IServiceCollection AddApi(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = ValidationErrorKeys.CamelCase);

        services.AddValidation();

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
