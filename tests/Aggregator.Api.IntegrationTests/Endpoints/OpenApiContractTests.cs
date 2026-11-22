using System.Net;
using System.Text.Json;
using Aggregator.Api.IntegrationTests.Infrastructure;

namespace Aggregator.Api.IntegrationTests.Endpoints;

/// <summary>The OpenAPI document describes the wire contract exactly.</summary>
public sealed class OpenApiContractTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    private static readonly string[] SecurityStatuses = ["401", "403", "429"];

    [Fact]
    public async Task Document_ShouldDeclareOpenApi32_WhenServed()
    {
        var document = await GetDocumentAsync();

        document.GetProperty("openapi").GetString().ShouldBe("3.2.0");
    }

    [Fact]
    public async Task AggregationParameters_ShouldUseWireNames_WhenDocumented()
    {
        var document = await GetDocumentAsync();

        var parameters = document.GetProperty("paths").GetProperty(ApiRoutes.Aggregation).GetProperty("get").GetProperty("parameters");

        parameters.EnumerateArray().Select(p => p.GetProperty("name").GetString()).ShouldBe(["q", "city", "sources", "category", "from", "to", "sortBy", "sortOrder", "page", "pageSize"]);
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

        using var response = await client.GetAsync($"{ApiRoutes.Aggregation}?pageSize=101", TestContext.Current.CancellationToken);

        var problem = await response.ShouldBeProblem(HttpStatusCode.BadRequest);
        problem.GetProperty("errors").EnumerateObject().Select(e => e.Name).ShouldBe(["pageSize"]);
    }

    [Fact]
    public async Task SecurityScheme_ShouldDescribeJwtBearer_WhenDocumented()
    {
        var document = await GetDocumentAsync();

        var scheme = BearerScheme(document);

        scheme.GetProperty("type").GetString().ShouldBe("http");
        scheme.GetProperty("scheme").GetString().ShouldBe("bearer");
        scheme.GetProperty("bearerFormat").GetString().ShouldBe("JWT");
    }

    [Theory]
    [InlineData(ApiRoutes.Aggregation, "aggregation:read")]
    [InlineData(ApiRoutes.AggregationStream, "aggregation:read")]
    [InlineData(ApiRoutes.Sources, "aggregation:read")]
    [InlineData(ApiRoutes.Statistics, "stats:read")]
    public async Task ProtectedOperations_ShouldListScopeAndAuthResponses_WhenDocumented(string route, string scope)
    {
        var document = await GetDocumentAsync();

        var operation = document.GetProperty("paths").GetProperty(route).GetProperty("get");

        var requirement = operation.GetProperty("security").EnumerateArray().ShouldHaveSingleItem();
        requirement.GetProperty("Bearer").EnumerateArray().Select(s => s.GetString()).ShouldBe([scope]);
        var responses = operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).ToList();
        SecurityStatuses.ShouldBeSubsetOf(responses);
    }

    [Theory]
    [InlineData("401")]
    [InlineData("403")]
    [InlineData("429")]
    public async Task SecurityResponses_ShouldBeProblemDetails_WhenDocumented(string status)
    {
        var document = await GetDocumentAsync();

        var response = SourcesResponse(document, status);

        response.GetProperty("content").EnumerateObject().Select(c => c.Name).ShouldBe(["application/problem+json"]);
        response.GetProperty("description").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TooManyRequests_ShouldDocumentRetryAfter_WhenDocumented()
    {
        var document = await GetDocumentAsync();

        var response = SourcesResponse(document, "429");

        response.GetProperty("headers").GetProperty("Retry-After").GetProperty("schema").GetProperty("type").GetString().ShouldBe("integer");
    }

    [Fact]
    public async Task SecurityScheme_ShouldNotCarryDevelopmentInstructions_WhenServed()
    {
        var document = await GetDocumentAsync();

        var scheme = BearerScheme(document);

        scheme.GetProperty("description").GetString().ShouldNotBeNull().ShouldNotContain("user-jwts");
    }

    private static JsonElement BearerScheme(JsonElement document) =>
        document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");

    private static JsonElement SourcesResponse(JsonElement document, string status) =>
        document.GetProperty("paths").GetProperty(ApiRoutes.Sources).GetProperty("get").GetProperty("responses").GetProperty(status);

    private async Task<JsonElement> GetDocumentAsync()
    {
        using var client = factory.CreateClient();
        return JsonElement.Parse(await client.GetStringAsync(ApiRoutes.OpenApiDocument, TestContext.Current.CancellationToken));
    }
}
