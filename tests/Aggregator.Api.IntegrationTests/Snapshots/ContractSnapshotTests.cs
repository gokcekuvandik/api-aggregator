using System.Net;
using Aggregator.Api.IntegrationTests.Infrastructure;

namespace Aggregator.Api.IntegrationTests.Snapshots;

/// <summary>
/// ADR-0002 confirmation: the OpenAPI document and the response shapes are pinned.
/// A contract change shows up as a diff in a <c>.verified.json</c> file.
/// </summary>
public sealed class ContractSnapshotTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    [Fact]
    public async Task OpenApiDocument_ShouldMatchSnapshot_WhenRequested()
    {
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync(ApiRoutes.OpenApiDocument, TestContext.Current.CancellationToken);

        await JsonSnapshot.MatchAsync(json);
    }

    [Fact]
    public async Task AggregationBody_ShouldMatchSnapshot_WhenOneSourceFails()
    {
        using var client = factory.CreateClientWith(
            StubDataSource.InState("news", "Success", itemCount: 2),
            StubDataSource.InState("weather", "Fallback"),
            StubDataSource.InState("repositories", "Failed"));

        var json = await client.GetStringAsync($"{ApiRoutes.Aggregation}?pageSize=2", TestContext.Current.CancellationToken);

        await JsonSnapshot.MatchAsync(json);
    }

    [Fact]
    public async Task AggregationBody_ShouldMatchSnapshot_WhenEverySourceFails()
    {
        using var client = factory.CreateClientWith(StubDataSource.InState("news", "Failed"));

        using var response = await client.GetAsync(ApiRoutes.Aggregation, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        await JsonSnapshot.MatchAsync(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ValidationBody_ShouldMatchSnapshot_WhenPageSizeIsTooLarge()
    {
        using var client = factory.CreateClientWith(StubDataSource.InState("news", "Success"));

        using var response = await client.GetAsync($"{ApiRoutes.Aggregation}?pageSize=101", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await JsonSnapshot.MatchAsync(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
