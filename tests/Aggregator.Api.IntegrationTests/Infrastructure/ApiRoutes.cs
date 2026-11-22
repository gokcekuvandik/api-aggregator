namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>The public routes under test, in one place.</summary>
internal static class ApiRoutes
{
    public const string Aggregation = "/api/v1/aggregation";
    public const string AggregationStream = "/api/v1/aggregation/stream";
    public const string Sources = "/api/v1/sources";
    public const string Statistics = "/api/v1/statistics";
    public const string OpenApiDocument = "/openapi/v1.json";
    public const string Liveness = "/health/live";
    public const string Readiness = "/health/ready";

    /// <summary>Every route that needs a token.</summary>
    public static TheoryData<string> Protected => [Aggregation, AggregationStream, Sources, Statistics];
}
