using System.Net;
using System.Net.Http.Headers;
using Aggregator.Application.Aggregation;
using Aggregator.Infrastructure.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;

namespace Aggregator.Infrastructure.UnitTests.TestDoubles;

/// <summary>A MockHttp handler plus a real <see cref="SourceFetcher"/>, which is what every adapter needs.</summary>
internal sealed class SourceTestHost : IDisposable
{
    public static readonly AggregationQuery Query = new(Page: 1, PageSize: 20);

    public SourceTestHost()
    {
        Factory = new StubHttpClientFactory(Http, StubHttpClientFactory.DefaultBaseAddress);
        Fetcher = new SourceFetcher(Factory, NullLogger<SourceFetcher>.Instance);
    }

    public MockHttpMessageHandler Http { get; } = new();

    public StubHttpClientFactory Factory { get; }

    public SourceFetcher Fetcher { get; }

    public static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>Expects a GitHub search answered with <paramref name="etag"/>, then a request revalidating it, answered 304.</summary>
    public static void ExpectGitHubSearchThenNotModified(MockHttpMessageHandler http, string etag)
    {
        ArgumentNullException.ThrowIfNull(http);
        http.Expect("*").Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Fixture("github-search.json"), MediaTypeHeaderValue.Parse("application/json")),
            };
            response.Headers.ETag = EntityTagHeaderValue.Parse(etag);
            return response;
        });
        http.Expect("*").WithHeaders("If-None-Match", etag).Respond(HttpStatusCode.NotModified);
    }

    public void Dispose() => Http.Dispose();
}
