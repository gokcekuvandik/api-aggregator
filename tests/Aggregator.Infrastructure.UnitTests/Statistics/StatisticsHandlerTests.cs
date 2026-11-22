using System.Net;
using Aggregator.Application.Statistics;
using Aggregator.Infrastructure.Statistics;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using NSubstitute;

namespace Aggregator.Infrastructure.UnitTests.Statistics;

public sealed class StatisticsHandlerTests
{
    private static readonly UpstreamApi Api = new("news", "hacker-news");
    private static readonly TimeSpan Upstream = TimeSpan.FromMilliseconds(120);

    private readonly ManualTimeProvider _clock = new(DateTimeOffset.UnixEpoch);
    private readonly ISourceTelemetry _telemetry = Substitute.For<ISourceTelemetry>();

    [Theory]
    [InlineData(HttpStatusCode.OK, RequestOutcome.Success)]
    [InlineData(HttpStatusCode.NoContent, RequestOutcome.Success)]
    [InlineData(HttpStatusCode.NotModified, RequestOutcome.Success)]
    [InlineData(HttpStatusCode.NotFound, RequestOutcome.Failure)]
    [InlineData(HttpStatusCode.TooManyRequests, RequestOutcome.Failure)]
    [InlineData(HttpStatusCode.ServiceUnavailable, RequestOutcome.Failure)]
    public async Task SendAsync_ShouldRecordTheDurationAndOutcome_WhenTheUpstreamAnswers(HttpStatusCode status, RequestOutcome expected)
    {
        using var invoker = Invoker(_ =>
        {
            _clock.Advance(Upstream);
            return new HttpResponseMessage(status);
        });

        using var response = await SendAsync(invoker);

        response.StatusCode.ShouldBe(status);
        _telemetry.Received(1).RecordRequest(Api, Upstream, expected, 1);
    }

    [Fact]
    public async Task SendAsync_ShouldCountEveryAttemptBelowIt_WhenTheRequestIsRetried()
    {
        using var invoker = Invoker(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            attemptsBeforeAnswer: 3);

        using var response = await SendAsync(invoker);

        _telemetry.Received(1).RecordRequest(Api, TimeSpan.Zero, RequestOutcome.Success, 3);
    }

    [Fact]
    public async Task SendAsync_ShouldIncludeReadingTheBody_WhenTheUpstreamStreamsItSlowly()
    {
        using var invoker = Invoker(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new SlowContent(_clock, Upstream) });

        using var response = await SendAsync(invoker);

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(SlowContent.Body);
        _telemetry.Received(1).RecordRequest(Api, Upstream, RequestOutcome.Success, 1);
    }

    [Fact]
    public async Task SendAsync_ShouldRecordAFailureAndRethrow_WhenTheRequestThrows()
    {
        using var invoker = Invoker(_ =>
        {
            _clock.Advance(Upstream);
            throw new HttpRequestException("connection refused");
        });

        await Should.ThrowAsync<HttpRequestException>(() => SendAsync(invoker));

        _telemetry.Received(1).RecordRequest(Api, Upstream, RequestOutcome.Failure, 1);
    }

    [Fact]
    public async Task SendAsync_ShouldRecordAFailure_WhenATimeoutCancelsTheRequestButNotTheCaller()
    {
        using var invoker = Invoker(_ => throw new TaskCanceledException("attempt timed out"));

        await Should.ThrowAsync<TaskCanceledException>(() => SendAsync(invoker));

        _telemetry.Received(1).RecordRequest(Api, TimeSpan.Zero, RequestOutcome.Failure, 1);
    }

    [Fact]
    public async Task SendAsync_ShouldRecordACancellation_WhenTheCallerCancels()
    {
        using var cts = new CancellationTokenSource();
        using var invoker = Invoker(_ =>
        {
            _clock.Advance(Upstream);
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });

        await Should.ThrowAsync<OperationCanceledException>(() => SendAsync(invoker, cts.Token));

        _telemetry.Received(1).RecordRequest(Api, Upstream, RequestOutcome.Cancelled, 1);
    }

    [Fact]
    public async Task SendAsync_ShouldRecordARejection_WhenNothingBelowItSentTheRequest()
    {
        var handler = new StatisticsHandler(_telemetry, Api, _clock)
        {
            InnerHandler = new DelegateHandler(_ => throw new InvalidOperationException("circuit open")),
        };
        using var invoker = new HttpMessageInvoker(handler);

        await Should.ThrowAsync<InvalidOperationException>(() => SendAsync(invoker));

        _telemetry.Received(1).RecordRequest(Api, TimeSpan.Zero, RequestOutcome.Rejected, 0);
    }

    [Fact]
    public async Task SendAsync_ShouldRecordOnce_WhenTheRecorderItselfThrows()
    {
        _telemetry.When(t => t.RecordRequest(Arg.Any<UpstreamApi>(), Arg.Any<TimeSpan>(), Arg.Any<RequestOutcome>(), Arg.Any<int>()))
            .Do(_ => throw new InvalidOperationException("recorder bug"));
        using var invoker = Invoker(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Should.ThrowAsync<InvalidOperationException>(() => SendAsync(invoker));

        _telemetry.ReceivedWithAnyArgs(1).RecordRequest(default, default, default, default);
    }

    [Fact]
    public async Task AttemptCountingHandler_ShouldPassTheRequestOn_WhenNoCounterWasAttached()
    {
        using var invoker = new HttpMessageInvoker(new AttemptCountingHandler { InnerHandler = new DelegateHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)) });

        using var response = await SendAsync(invoker);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /// <summary>The statistics handler over an attempt counter that sends <paramref name="attemptsBeforeAnswer"/> times, as a retrying pipeline would.</summary>
    private HttpMessageInvoker Invoker(Func<HttpRequestMessage, HttpResponseMessage> answer, int attemptsBeforeAnswer = 1)
    {
        var counter = new AttemptCountingHandler { InnerHandler = new DelegateHandler(answer) };
        var retries = new RepeatingHandler(attemptsBeforeAnswer) { InnerHandler = counter };
        return new HttpMessageInvoker(new StatisticsHandler(_telemetry, Api, _clock) { InnerHandler = retries });
    }

    private static Task<HttpResponseMessage> SendAsync(HttpMessageInvoker invoker, CancellationToken? cancellationToken = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri("https://source.test/search"));
        return invoker.SendAsync(request, cancellationToken ?? TestContext.Current.CancellationToken);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }

    /// <summary>Sends the request <c>times</c> times and returns the last response, disposing the others.</summary>
    private sealed class RepeatingHandler(int times) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            for (var attempt = 1; attempt < times; attempt++)
            {
                (await base.SendAsync(request, cancellationToken)).Dispose();
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>A body that takes <c>delay</c> on the test clock to arrive.</summary>
    private sealed class SlowContent(ManualTimeProvider clock, TimeSpan delay) : HttpContent
    {
        public const string Body = "slow body";

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            clock.Advance(delay);
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(Body));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }
}
