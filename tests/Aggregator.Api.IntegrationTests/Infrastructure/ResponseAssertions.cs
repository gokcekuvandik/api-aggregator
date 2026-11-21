using System.Net;
using System.Text.Json;

namespace Aggregator.Api.IntegrationTests.Infrastructure;

internal static class ResponseAssertions
{
    public static async Task<JsonElement> ShouldBeJson(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(expected, body);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Asserts an RFC 9457 body that carries a trace id (ADR-0002).</summary>
    public static async Task<JsonElement> ShouldBeProblem(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(expected, body);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json", body);

        var root = JsonDocument.Parse(body).RootElement.Clone();
        root.GetProperty("status").GetInt32().ShouldBe((int)expected);
        root.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
        root.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        return root;
    }
}
