using System.Diagnostics;
using System.Net;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Aggregator.Infrastructure.Sources;
using Aggregator.Infrastructure.Sources.Caching;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using RichardSzalay.MockHttp;

namespace Aggregator.Infrastructure.IntegrationTests;

/// <summary>Replicas that share a real Redis second level, as the scaled-out topology runs them.</summary>
public sealed class RedisSecondLevelCacheTests(RedisFixture redis)
{
    private static readonly AggregationQuery Query = new(Page: 1, PageSize: 20);

    private static readonly string GitHubSearch = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "github-search.json"));

    /// <summary>One Redis operation timeout, which a request that waited on a paused Redis cannot beat.</summary>
    private static readonly TimeSpan FastAnswer = TimeSpan.FromMilliseconds(SecondLevelCacheServiceCollectionExtensions.OperationTimeoutMs);

    /// <summary>Fresh answers are not cached, so the second replica must call upstream and can only fall back to last-known-good items.</summary>
    private static readonly Dictionary<string, string?> Uncached = new() { [$"Sources:{SourceNames.Repositories}:Cache:Enabled"] = "false" };

    [Fact]
    public async Task AddInfrastructure_ShouldServeAnotherReplicaFromRedis_WhenOneReplicaFetchedFirst()
    {
        await redis.ResetAsync();
        using var http = AnswersOnce();
        await using var first = Replica(http);
        await using var second = Replica(http);

        var fetched = await Fetch(first);
        var served = await Fetch(second);

        http.VerifyNoOutstandingExpectation();
        served.ShouldBeOfType<SourceCached>().Items.ShouldBe(fetched.ShouldBeOfType<SourceSuccess>().Items);
    }

    [Fact]
    public async Task AddInfrastructure_ShouldFallBackToAnotherReplicasLastKnownGood_WhenTheUpstreamFails()
    {
        await redis.ResetAsync();
        using var healthy = AnswersOnce();
        using var failing = new MockHttpMessageHandler();
        failing.When("*").Respond(HttpStatusCode.NotFound);
        await using var first = Replica(healthy, Uncached);
        await using var second = Replica(failing, Uncached);

        var fetched = await Fetch(first);
        var fallback = await Fetch(second);

        fallback.ShouldBeOfType<SourceFallback>().Items.ShouldBe(fetched.ShouldBeOfType<SourceSuccess>().Items);
    }

    [Fact]
    public async Task AddInfrastructure_ShouldWriteKeysUnderTheInstancePrefix_WhenAReplicaCachesAnAnswer()
    {
        await redis.ResetAsync();
        using var http = AnswersOnce();
        await using var replica = Replica(http);

        await Fetch(replica);

        var keys = RedisKeys();
        keys.ShouldNotBeEmpty();
        keys.ShouldAllBe(key => key.StartsWith(SecondLevelCacheServiceCollectionExtensions.InstanceName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddInfrastructure_ShouldSkipRedisAfterTheFirstFailure_WhenRedisIsPaused()
    {
        await redis.ResetAsync();
        using var http = AlwaysAnswers();
        await using var replica = Replica(http);
        await Fetch(replica);

        var (first, next, elapsed) = await redis.WhilePausedAsync(async () =>
        {
            var first = await Fetch(replica, Query with { Text = "first" });
            var clock = Stopwatch.StartNew();
            var next = await Fetch(replica, Query with { Text = "next" });
            return (first, next, clock.Elapsed);
        });

        first.ShouldBeOfType<SourceSuccess>();
        next.ShouldBeOfType<SourceSuccess>();
        BreakerWarnings(replica).ShouldBe(1);
        elapsed.ShouldBeLessThan(FastAnswer);
    }

    [Fact]
    public async Task AddInfrastructure_ShouldWriteToRedisAgain_WhenRedisIsBackAfterTheBreak()
    {
        await redis.ResetAsync();
        using var http = AlwaysAnswers();
        var time = new FakeTimeProvider();
        await using var replica = Replica(http, time: time);
        await Fetch(replica);

        await redis.WhilePausedAsync(() => Fetch(replica, Query with { Text = "during" }));
        time.Advance(CircuitBreakingDistributedCache.BreakDuration);
        var result = await Fetch(replica, Query with { Text = "after" });

        result.ShouldBeOfType<SourceSuccess>();
        RedisKeys().ShouldContain(key => key.Contains("after", StringComparison.Ordinal));
        RedisKeys().ShouldNotContain(key => key.Contains("during", StringComparison.Ordinal));
    }

    /// <summary>Answers one request; a second one fails, so a test can prove the other replica never called upstream.</summary>
    private static MockHttpMessageHandler AnswersOnce()
    {
        var http = new MockHttpMessageHandler();
        http.Expect("*").Respond("application/json", GitHubSearch);
        return http;
    }

    private static MockHttpMessageHandler AlwaysAnswers()
    {
        var http = new MockHttpMessageHandler();
        http.When("*").Respond("application/json", GitHubSearch);
        return http;
    }

    private static int BreakerWarnings(ServiceProvider replica) =>
        replica.GetFakeLogCollector().GetSnapshot()
            .Count(entry => entry.Category == typeof(CircuitBreakingDistributedCache).FullName && entry.Level == LogLevel.Warning);

    private List<string> RedisKeys() => [.. redis.Server.Keys(pattern: "*").Select(key => key.ToString())];

    private ServiceProvider Replica(MockHttpMessageHandler http, Dictionary<string, string?>? settings = null, TimeProvider? time = null) =>
        TestServices.BuildInfrastructure(
            new(settings ?? []) { [$"ConnectionStrings:{SecondLevelCacheServiceCollectionExtensions.ConnectionStringName}"] = redis.ConnectionString },
            services =>
            {
                services.AddLogging(logging => logging.AddFakeLogging());
                services.AddSingleton(time ?? TimeProvider.System);
                services.AddHttpClient(SourceNames.Repositories).ConfigurePrimaryHttpMessageHandler(() => http);
            });

    private static Task<SourceResult> Fetch(ServiceProvider provider, AggregationQuery? query = null) =>
        provider.GetServices<IDataSource>().Single(source => source.Name == SourceNames.Repositories)
            .FetchAsync(query ?? Query, TestContext.Current.CancellationToken);
}
