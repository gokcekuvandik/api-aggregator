namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>The logger categories the tests read, in one place.</summary>
internal static class LogCategories
{
    public const string HttpLogging = "Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware";
    public const string EndpointLogging = "Aggregator.Api.Http.EndpointLoggingInterceptor";
}
