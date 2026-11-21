using System.Net;
using Aggregator.Infrastructure.Sources.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using RichardSzalay.MockHttp;

namespace Aggregator.Infrastructure.UnitTests.Sources.Http;

public sealed class RateLimitLoggingHandlerTests : IDisposable
{
    private readonly MockHttpMessageHandler _http = new();
    private readonly FakeLogger<RateLimitLoggingHandler> _logger = new();
    private readonly HttpMessageInvoker _invoker;

    public RateLimitLoggingHandlerTests() =>
        _invoker = new HttpMessageInvoker(new RateLimitLoggingHandler(_logger, "repositories") { InnerHandler = _http });

    public void Dispose()
    {
        _invoker.Dispose();
        _http.Dispose();
    }

    [Theory]
    [InlineData("6", LogLevel.Debug)]
    [InlineData("59", LogLevel.Debug)]
    [InlineData("5", LogLevel.Warning)]
    [InlineData("0", LogLevel.Warning)]
    public async Task SendAsync_ShouldLogRemainingQuota_WhenRateLimitHeadersArePresent(string remaining, LogLevel expected)
    {
        _http.When("*").Respond(_ => Response(HttpStatusCode.OK, [new("X-RateLimit-Remaining", remaining), new("X-RateLimit-Reset", "1795262400")]));

        using var response = await SendAsync();

        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(expected);
        record.Message.ShouldContain("repositories");
        record.Message.ShouldContain(remaining);
        record.Message.ShouldContain("1795262400");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-number")]
    public async Task SendAsync_ShouldNotLog_WhenRemainingHeaderIsMissingOrInvalid(string? remaining)
    {
        _http.When("*").Respond(_ => Response(HttpStatusCode.OK, remaining is null ? [] : [new("X-RateLimit-Remaining", remaining)]));

        using var response = await SendAsync();

        _logger.Collector.Count.ShouldBe(0);
    }

    [Fact]
    public async Task SendAsync_ShouldLogRetryAfter_WhenSourceAnswersTooManyRequests()
    {
        _http.When("*").Respond(_ => Response(HttpStatusCode.TooManyRequests, [new("Retry-After", "30")]));

        using var response = await SendAsync();

        var record = _logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.Message.ShouldContain("00:00:30");
    }

    [Theory]
    [InlineData("Sat, 21 Nov 2026 12:00:00 GMT", "2026-11-21T12:00:00.0000000+00:00")]
    [InlineData(null, "unknown")]
    public async Task SendAsync_ShouldLogRetryAfterDateOrUnknown_WhenTooManyRequestsHasNoDelay(string? retryAfter, string expected)
    {
        _http.When("*").Respond(_ => Response(HttpStatusCode.TooManyRequests, retryAfter is null ? [] : [new("Retry-After", retryAfter)]));

        using var response = await SendAsync();

        _logger.Collector.GetSnapshot().ShouldHaveSingleItem().Message.ShouldContain(expected);
    }

    [Fact]
    public async Task SendAsync_ShouldReturnResponseUnchanged_WhenHeadersAreLogged()
    {
        _http.When("*").Respond(_ =>
        {
            var response = Response(HttpStatusCode.Accepted, [new("X-RateLimit-Remaining", "10")]);
            response.Content = new StringContent("body");
            return response;
        });

        using var response = await SendAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("body");
    }

    private async Task<HttpResponseMessage> SendAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://source.test/items");
        return await _invoker.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, IEnumerable<KeyValuePair<string, string>> headers)
    {
        var response = new HttpResponseMessage(status);
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        return response;
    }
}
