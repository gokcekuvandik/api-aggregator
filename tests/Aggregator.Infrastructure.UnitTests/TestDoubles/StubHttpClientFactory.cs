namespace Aggregator.Infrastructure.UnitTests.TestDoubles;

/// <summary>Hands out clients over one handler, so tests can script responses with MockHttp.</summary>
internal sealed class StubHttpClientFactory(HttpMessageHandler handler, Uri baseAddress) : IHttpClientFactory
{
    public static readonly Uri DefaultBaseAddress = new("https://source.test/");

    public List<string> RequestedNames { get; } = [];

    public HttpClient CreateClient(string name)
    {
        RequestedNames.Add(name);
        return new HttpClient(handler, disposeHandler: false) { BaseAddress = baseAddress };
    }
}
