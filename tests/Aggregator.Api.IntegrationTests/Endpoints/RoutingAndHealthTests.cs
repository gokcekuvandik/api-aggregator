using System.Net;
using Aggregator.Api.IntegrationTests.Infrastructure;

namespace Aggregator.Api.IntegrationTests.Endpoints;

/// <summary>ADR-0002: URL-segment versioning, ProblemDetails for unknown routes, health probes.</summary>
public sealed class RoutingAndHealthTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    [Fact]
    public async Task Get_ShouldReportSupportedVersions_WhenVersionIsV1()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(ApiRoutes.Sources, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("api-supported-versions").ShouldContain("1.0");
    }

    [Fact]
    public async Task Get_ShouldReturn404Problem_WhenVersionSegmentIsMissing()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/sources", TestContext.Current.CancellationToken);

        await response.ShouldBeProblem(HttpStatusCode.NotFound);
    }

    /// <summary>With a URL-segment version, an unknown version is a URI that does not exist.</summary>
    [Fact]
    public async Task Get_ShouldReturn404Problem_WhenVersionIsUnsupported()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v2/sources", TestContext.Current.CancellationToken);

        await response.ShouldBeProblem(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Get_ShouldReturnHealthy_WhenProbeIsCalled(string path)
    {
        using var client = factory.CreateProductionClientWith();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Get_ShouldReturn404_WhenOldHealthPathIsCalled(string path)
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
