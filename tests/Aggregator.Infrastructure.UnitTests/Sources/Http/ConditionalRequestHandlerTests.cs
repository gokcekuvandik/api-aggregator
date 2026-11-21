using System.Net;
using System.Net.Http.Headers;
using Aggregator.Infrastructure.Sources.Http;
using RichardSzalay.MockHttp;

namespace Aggregator.Infrastructure.UnitTests.Sources.Http;

public sealed class ConditionalRequestHandlerTests : IDisposable
{
    private const string Url = "https://api.github.test/search?q=dotnet";
    private const string ETag = "\"abc123\"";

    private readonly MockHttpMessageHandler _http = new();
    private readonly ConditionalResponseStore _store = new();
    private readonly HttpMessageInvoker _invoker;

    public ConditionalRequestHandlerTests() =>
        _invoker = new HttpMessageInvoker(new ConditionalRequestHandler(_store) { InnerHandler = _http });

    public void Dispose()
    {
        _invoker.Dispose();
        _http.Dispose();
    }

    [Fact]
    public async Task SendAsync_ShouldRefuseBody_WhenItExceedsTheResponseSizeCap()
    {
        var oversized = new byte[4 * 1024 * 1024 + 1];
        _http.When(Url).Respond(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(oversized), Headers = { ETag = new EntityTagHeaderValue(ETag) } });

        await Should.ThrowAsync<HttpRequestException>(GetAsync);
        _store.TryGet(Url, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task SendAsync_ShouldSendNoValidator_WhenNothingIsStored()
    {
        var conditional = true;
        _http.When(Url).With(request => { conditional = request.Headers.IfNoneMatch.Count > 0; return true; }).Respond(_ => Ok("first"));

        using var response = await GetAsync();

        conditional.ShouldBeFalse();
    }

    [Fact]
    public async Task SendAsync_ShouldSendStoredETag_WhenSameUrlIsRequestedAgain()
    {
        _http.Expect(Url).Respond(_ => Ok("first"));
        _http.Expect(Url).WithHeaders("If-None-Match", ETag).Respond(_ => Ok("second"));

        using (await GetAsync())
        {
        }

        using var second = await GetAsync();

        _http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task SendAsync_ShouldReplayStoredBodyAsOk_WhenSourceAnswersNotModified()
    {
        _http.Expect(Url).Respond(_ => Ok("""{"items":[1]}"""));
        _http.Expect(Url).Respond(HttpStatusCode.NotModified);
        using (await GetAsync())
        {
        }

        using var replayed = await GetAsync();

        replayed.StatusCode.ShouldBe(HttpStatusCode.OK);
        replayed.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        (await replayed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("""{"items":[1]}""");
    }

    [Fact]
    public async Task SendAsync_ShouldStillReturnBody_WhenResponseIsStored()
    {
        _http.When(Url).Respond(_ => Ok("first"));

        using var response = await GetAsync();

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("first");
    }

    [Fact]
    public async Task SendAsync_ShouldPassNotModifiedThrough_WhenNothingIsStored()
    {
        _http.When(Url).Respond(HttpStatusCode.NotModified);

        using var response = await GetAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, false)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    public async Task SendAsync_ShouldNotStore_WhenResponseHasNoETagOrFailed(HttpStatusCode status, bool withETag)
    {
        _http.Expect(Url).Respond(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("x") };
            if (withETag)
            {
                response.Headers.ETag = EntityTagHeaderValue.Parse(ETag);
            }

            return response;
        });
        var conditional = true;
        _http.Expect(Url).With(request => { conditional = request.Headers.IfNoneMatch.Count > 0; return true; }).Respond(_ => Ok("next"));
        using (await GetAsync())
        {
        }

        using var second = await GetAsync();

        conditional.ShouldBeFalse();
    }

    [Fact]
    public async Task SendAsync_ShouldNotAddSecondValidator_WhenSameRequestIsRetried()
    {
        _http.Expect(Url).Respond(_ => Ok("first"));
        _http.When(Url).Respond(HttpStatusCode.NotModified);
        using (await GetAsync())
        {
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        using (await _invoker.SendAsync(request, TestContext.Current.CancellationToken))
        {
        }

        using (await _invoker.SendAsync(request, TestContext.Current.CancellationToken))
        {
        }

        request.Headers.IfNoneMatch.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_ShouldIgnoreNonGetRequests_WhenStoreHasEntry()
    {
        _http.Expect(Url).Respond(_ => Ok("first"));
        var conditional = true;
        _http.Expect(HttpMethod.Post, Url).With(request => { conditional = request.Headers.IfNoneMatch.Count > 0; return true; }).Respond(HttpStatusCode.Created);
        using (await GetAsync())
        {
        }

        using var post = new HttpRequestMessage(HttpMethod.Post, Url);
        using var response = await _invoker.SendAsync(post, TestContext.Current.CancellationToken);

        conditional.ShouldBeFalse();
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public void Store_ShouldEvictLeastRecentlyUsedKey_WhenCapacityIsReached()
    {
        var store = new ConditionalResponseStore(capacity: 2);
        store.Set("a", Entry("1"));
        store.Set("b", Entry("2"));
        store.TryGet("a", out _);

        store.Set("c", Entry("3"));

        store.TryGet("b", out _).ShouldBeFalse();
        store.TryGet("a", out _).ShouldBeTrue();
        store.TryGet("c", out _).ShouldBeTrue();
    }

    [Fact]
    public void Store_ShouldReplaceEntryWithoutEvicting_WhenKeyIsKnown()
    {
        var store = new ConditionalResponseStore(capacity: 2);
        store.Set("a", Entry("1"));
        store.Set("b", Entry("2"));

        store.Set("a", Entry("4"));

        store.TryGet("b", out _).ShouldBeTrue();
        store.TryGet("a", out var updated).ShouldBeTrue();
        updated!.ETag.Tag.ShouldBe("\"4\"");
    }

    [Fact]
    public void Store_ShouldKeepAcceptingNewKeys_WhenManyDistinctUrlsArrive()
    {
        var store = new ConditionalResponseStore(capacity: 2);

        foreach (var i in Enumerable.Range(0, 10))
        {
            store.Set($"k{i}", Entry(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        store.TryGet("k9", out _).ShouldBeTrue();
        store.TryGet("k8", out _).ShouldBeTrue();
        store.TryGet("k0", out _).ShouldBeFalse();
    }

    private static StoredResponse Entry(string etag) => new(new EntityTagHeaderValue($"\"{etag}\""), [], "application/json");

    private async Task<HttpResponseMessage> GetAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        return await _invoker.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static HttpResponseMessage Ok(string body)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, MediaTypeHeaderValue.Parse("application/json")) };
        response.Headers.ETag = EntityTagHeaderValue.Parse(ETag);
        return response;
    }
}
