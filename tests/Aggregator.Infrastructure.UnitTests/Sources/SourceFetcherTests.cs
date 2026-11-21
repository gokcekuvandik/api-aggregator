using System.Net;
using System.Text.Json.Serialization;
using Aggregator.Application.Sources;
using Aggregator.Domain.Items;
using Aggregator.Infrastructure.Sources;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Polly.CircuitBreaker;
using Polly.Timeout;
using RichardSzalay.MockHttp;

namespace Aggregator.Infrastructure.UnitTests.Sources;

public sealed class SourceFetcherTests : IDisposable
{
    private const string SourceName = "test";
    private const string Path = "items";
    private const string InternalDetail = "secret-host.internal:8443";

    private readonly MockHttpMessageHandler _http = new();
    private readonly FakeLogger<SourceFetcher> _logger = new();
    private readonly StubHttpClientFactory _factory;
    private readonly SourceFetcher _fetcher;

    public SourceFetcherTests()
    {
        _factory = new StubHttpClientFactory(_http, StubHttpClientFactory.DefaultBaseAddress);
        _fetcher = new SourceFetcher(_factory, _logger);
    }

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task FetchAsync_ShouldReturnMappedItems_WhenSourceAnswersOk()
    {
        _http.When($"{StubHttpClientFactory.DefaultBaseAddress}{Path}").Respond("application/json", """{"names":["a","b"]}""");

        var result = await FetchAsync(ToItems, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceSuccess>().Items.Select(i => i.Title).ShouldBe(["a", "b"]);
        _factory.RequestedNames.ShouldBe([SourceName]);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FetchAsync_ShouldReturnFailureWithStatusCode_WhenSourceAnswersError(HttpStatusCode status)
    {
        _http.When("*").Respond(status);

        var result = await FetchAsync(ToItems, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceFailure>().Error.ShouldBe($"test answered HTTP {(int)status}.");
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnUnreachableFailure_WhenConnectionFails()
    {
        _http.When("*").Throw(new HttpRequestException($"No such host {InternalDetail}"));

        var result = await FetchAsync(ToItems, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceFailure>().Error.ShouldBe("test could not be reached.");
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnUnreachableFailure_WhenConnectionDropsMidBody()
    {
        _http.When("*").Throw(new HttpIOException(HttpRequestError.ResponseEnded, "The response ended prematurely."));

        var result = await FetchAsync(ToItems, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceFailure>().Error.ShouldBe("test could not be reached.");
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnTimeoutFailure_WhenHttpClientTimesOut()
    {
        _http.When("*").Throw(new TaskCanceledException("timeout", new TimeoutException()));

        var result = await FetchAsync(ToItems, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceFailure>().Error.ShouldBe("test timed out.");
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnTimeoutFailure_WhenResilienceTimeoutRejects()
    {
        _http.When("*").Throw(new TimeoutRejectedException());

        var result = await FetchAsync(ToItems, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceFailure>().Error.ShouldBe("test timed out.");
    }

    [Fact]
    public async Task FetchAsync_ShouldReturnUnavailableFailure_WhenCircuitIsOpen()
    {
        _http.When("*").Throw(new BrokenCircuitException());

        var result = await FetchAsync(ToItems, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceFailure>().Error.ShouldBe("test is temporarily unavailable.");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("""{"names":"not-an-array"}""")]
    public async Task FetchAsync_ShouldReturnUnreadableFailure_WhenBodyDoesNotMatchContract(string body)
    {
        _http.When("*").Respond("application/json", body);

        var result = await FetchAsync(ToItems, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceFailure>().Error.ShouldBe("test returned an unreadable response.");
    }

    [Fact]
    public async Task FetchAsync_ShouldLogWarningWithDetails_WhenSourceFails()
    {
        _http.When("*").Throw(new HttpRequestException($"No such host {InternalDetail}"));

        var result = await FetchAsync(ToItems, TestContext.Current.CancellationToken);

        result.ShouldBeOfType<SourceFailure>().Error.ShouldNotContain(InternalDetail);
        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.Exception.ShouldNotBeNull().Message.ShouldContain(InternalDetail);
        record.Message.ShouldContain(SourceName);
    }

    [Fact]
    public async Task FetchAsync_ShouldThrow_WhenCallerCancels()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _http.When("*").Respond("application/json", """{"names":[]}""");

        await Should.ThrowAsync<OperationCanceledException>(() => FetchAsync(ToItems, cts.Token));
    }

    [Fact]
    public async Task FetchAsync_ShouldThrow_WhenPipelineFailsUnexpectedly()
    {
        _http.When("*").Throw(new InvalidOperationException("handler bug"));

        await Should.ThrowAsync<InvalidOperationException>(() => FetchAsync(ToItems, TestContext.Current.CancellationToken));
        _logger.Collector.Count.ShouldBe(0);
    }

    [Fact]
    public async Task FetchAsync_ShouldThrow_WhenMappingFails()
    {
        _http.When("*").Respond("application/json", """{"names":["a"]}""");

        await Should.ThrowAsync<InvalidOperationException>(() =>
            FetchAsync(_ => throw new InvalidOperationException("mapping bug"), TestContext.Current.CancellationToken));
    }

    private Task<SourceResult> FetchAsync(Func<NamesPayload, IReadOnlyList<AggregatedItem>> map, CancellationToken cancellationToken) =>
        _fetcher.FetchAsync(SourceName, new HttpRequestMessage(HttpMethod.Get, Path), TestPayloadJsonContext.Default.NamesPayload, map, cancellationToken);

    private static IReadOnlyList<AggregatedItem> ToItems(NamesPayload payload) =>
        [.. payload.Names.Select(name => new AggregatedItem(
            name, SourceName, SourceName, name, null, null, DateTimeOffset.UnixEpoch, RelevanceScore.None, new NewsDetails(null, null, null)))];
}

internal sealed record NamesPayload(IReadOnlyList<string> Names);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NamesPayload))]
internal sealed partial class TestPayloadJsonContext : JsonSerializerContext;
