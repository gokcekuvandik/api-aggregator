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

    public void Dispose() => Http.Dispose();
}
