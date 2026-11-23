using Aggregator.Infrastructure.Sources.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Aggregator.Infrastructure.UnitTests.Sources.Caching;

public sealed class CircuitBreakingDistributedCacheTests
{
    private const string Key = "key";
    private static readonly byte[] Value = [1, 2, 3];

    private readonly IDistributedCache inner = Substitute.For<IDistributedCache>();
    private readonly FakeTimeProvider time = new();
    private readonly FakeLogger<CircuitBreakingDistributedCache> logger = new();

    [Fact]
    public async Task GetAsync_ShouldReturnTheInnerValue_WhenTheInnerCacheAnswers()
    {
        inner.GetAsync(Key, Arg.Any<CancellationToken>()).Returns(Value);

        var value = await Cache().GetAsync(Key, TestContext.Current.CancellationToken);

        value.ShouldBe(Value);
    }

    [Fact]
    public async Task GetAsync_ShouldRethrowTheFailure_WhenTheInnerCacheFailsFirst()
    {
        inner.GetAsync(Key, Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("redis"));

        await Should.ThrowAsync<TimeoutException>(() => Cache().GetAsync(Key, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_ShouldMissWithoutCallingTheInnerCache_WhenTheCircuitIsOpen()
    {
        var cache = await OpenCircuitAsync();

        var value = await cache.GetAsync(Key, TestContext.Current.CancellationToken);

        value.ShouldBeNull();
        inner.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task WriteCalls_ShouldSkipTheInnerCache_WhenTheCircuitIsOpen()
    {
        var cache = await OpenCircuitAsync();

        await cache.SetAsync(Key, Value, new DistributedCacheEntryOptions(), TestContext.Current.CancellationToken);
        await cache.RefreshAsync(Key, TestContext.Current.CancellationToken);
        await cache.RemoveAsync(Key, TestContext.Current.CancellationToken);

        inner.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task SyncCalls_ShouldSkipTheInnerCache_WhenTheCircuitIsOpen()
    {
        var cache = await OpenCircuitAsync();

        cache.Get(Key).ShouldBeNull();
        cache.Set(Key, Value, new DistributedCacheEntryOptions());
        cache.Refresh(Key);
        cache.Remove(Key);

        inner.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void SyncCalls_ShouldReachTheInnerCache_WhenTheCircuitIsClosed()
    {
        inner.Get(Key).Returns(Value);
        var cache = Cache();

        cache.Get(Key).ShouldBe(Value);
        cache.Set(Key, Value, new DistributedCacheEntryOptions());
        cache.Refresh(Key);
        cache.Remove(Key);

        inner.ReceivedCalls().Count().ShouldBe(4);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 1)]
    public async Task GetAsync_ShouldCallTheInnerCacheAgain_WhenTheBreakDurationHasPassed(int ticksFromEnd, int expectedCalls)
    {
        var cache = await OpenCircuitAsync();
        inner.GetAsync(Key, Arg.Any<CancellationToken>()).Returns(Value);

        time.Advance(CircuitBreakingDistributedCache.BreakDuration + TimeSpan.FromTicks(ticksFromEnd));
        await cache.GetAsync(Key, TestContext.Current.CancellationToken);

        await inner.Received(expectedCalls).GetAsync(Key, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_ShouldOpenAgain_WhenTheRetryAfterTheBreakFails()
    {
        var cache = await OpenCircuitAsync();
        time.Advance(CircuitBreakingDistributedCache.BreakDuration);

        await Should.ThrowAsync<TimeoutException>(() => cache.GetAsync(Key, TestContext.Current.CancellationToken));
        var value = await cache.GetAsync(Key, TestContext.Current.CancellationToken);

        value.ShouldBeNull();
        await inner.Received(1).GetAsync(Key, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_ShouldStayClosed_WhenTheCallerCancels()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        inner.GetAsync(Key, cancelled.Token).ThrowsAsync(new OperationCanceledException(cancelled.Token));
        inner.GetAsync(Key, TestContext.Current.CancellationToken).Returns(Value);
        var cache = Cache();

        await Should.ThrowAsync<OperationCanceledException>(() => cache.GetAsync(Key, cancelled.Token));
        var value = await cache.GetAsync(Key, TestContext.Current.CancellationToken);

        value.ShouldBe(Value);
    }

    [Fact]
    public void Get_ShouldLogOneWarning_WhenTheCircuitOpens()
    {
        inner.Get(Key).Throws(new TimeoutException("redis"));
        var cache = Cache();

        Should.Throw<TimeoutException>(() => cache.Get(Key));
        cache.Get(Key);

        var entry = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Exception.ShouldBeOfType<TimeoutException>();
    }

    [Fact]
    public async Task GetAsync_ShouldLogOneWarning_WhenConcurrentCallsFail()
    {
        var pending = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        inner.GetAsync(Key, Arg.Any<CancellationToken>()).Returns(pending.Task);
        var cache = Cache();

        var first = cache.GetAsync(Key, TestContext.Current.CancellationToken);
        var second = cache.GetAsync(Key, TestContext.Current.CancellationToken);
        pending.SetException(new TimeoutException("redis"));

        await Should.ThrowAsync<TimeoutException>(() => first);
        await Should.ThrowAsync<TimeoutException>(() => second);
        logger.Collector.GetSnapshot().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GetAsync_ShouldSendOneProbe_WhenTheBreakHasPassed()
    {
        var cache = await OpenCircuitAsync();
        var probe = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        inner.GetAsync(Key, Arg.Any<CancellationToken>()).Returns(probe.Task);
        time.Advance(CircuitBreakingDistributedCache.BreakDuration);

        var probing = cache.GetAsync(Key, TestContext.Current.CancellationToken);
        var skipped = await cache.GetAsync(Key, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        probe.SetResult(Value);

        skipped.ShouldBeNull();
        (await probing).ShouldBe(Value);
        await inner.Received(1).GetAsync(Key, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_ShouldCloseTheCircuit_WhenTheProbeSucceeds()
    {
        var cache = await OpenCircuitAsync();
        inner.GetAsync(Key, Arg.Any<CancellationToken>()).Returns(Value);
        time.Advance(CircuitBreakingDistributedCache.BreakDuration);

        await cache.GetAsync(Key, TestContext.Current.CancellationToken);
        var value = await cache.GetAsync(Key, TestContext.Current.CancellationToken);

        value.ShouldBe(Value);
        await inner.Received(2).GetAsync(Key, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Dispose_ShouldDisposeTheInnerCache_WhenTheInnerCacheIsDisposable()
    {
        var disposable = Substitute.For<IDistributedCache, IDisposable>();

        new CircuitBreakingDistributedCache(disposable, time, logger).Dispose();

        ((IDisposable)disposable).Received(1).Dispose();
    }

    private CircuitBreakingDistributedCache Cache() => new(inner, time, logger);

    /// <summary>A cache whose first call failed, with the received calls cleared so tests count only what follows.</summary>
    private async Task<CircuitBreakingDistributedCache> OpenCircuitAsync()
    {
        inner.GetAsync(Key, Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("redis"));
        var cache = Cache();
        await Should.ThrowAsync<TimeoutException>(() => cache.GetAsync(Key, TestContext.Current.CancellationToken));
        inner.ClearReceivedCalls();
        return cache;
    }
}
