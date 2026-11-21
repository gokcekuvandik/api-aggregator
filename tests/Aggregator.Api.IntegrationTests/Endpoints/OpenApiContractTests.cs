using System.Net;
using System.Text.Json;
using Aggregator.Api.IntegrationTests.Infrastructure;

namespace Aggregator.Api.IntegrationTests.Endpoints;

/// <summary>ADR-0002 driver: the OpenAPI document describes the wire contract exactly.</summary>
public sealed class OpenApiContractTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    [Fact]
    public async Task AggregationParameters_ShouldUseWireNames_WhenDocumented()
    {
        var document = await GetDocumentAsync();

        var parameters = document.GetProperty("paths").GetProperty("/api/v1/aggregation").GetProperty("get").GetProperty("parameters");

        parameters.EnumerateArray().Select(p => p.GetProperty("name").GetString()).ShouldBe(["page", "pageSize"]);
    }

    [Fact]
    public async Task IntegerFields_ShouldBeDocumentedAsPlainIntegers_WhenInResponses()
    {
        var document = await GetDocumentAsync();

        var page = document.GetProperty("components").GetProperty("schemas").GetProperty("PagingResponse").GetProperty("properties").GetProperty("page");

        page.GetProperty("type").ValueKind.ShouldBe(JsonValueKind.String);
        page.GetProperty("type").GetString().ShouldBe("integer");
    }

    [Fact]
    public async Task ValidationErrors_ShouldBeKeyedByQueryParameterName_WhenPageSizeIsInvalid()
    {
        using var client = factory.CreateClientWith(StubDataSource.InState("news", "Success"));

        using var response = await client.GetAsync("/api/v1/aggregation?pageSize=101", TestContext.Current.CancellationToken);

        var problem = await response.ShouldBeProblem(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").EnumerateObject().Select(e => e.Name).ShouldBe(["pageSize"]);
    }

    private async Task<JsonElement> GetDocumentAsync()
    {
        using var client = factory.CreateClient();
        return JsonElement.Parse(await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken));
    }
}
