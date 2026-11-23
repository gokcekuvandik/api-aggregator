using System.Net;
using Aggregator.Application.Sources;
using Aggregator.Infrastructure.Sources;
using Aggregator.Infrastructure.Sources.Caching;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;
using StackExchange.Redis;

namespace Aggregator.Infrastructure.UnitTests.Sources.Caching;

public sealed class SecondLevelCacheTests
{
    private const string ConnectionStringKey = $"ConnectionStrings:{SecondLevelCacheServiceCollectionExtensions.ConnectionStringName}";

    /// <summary>The HybridCache key of <see cref="SourceTestHost.Query"/> for repositories, as Redis would hold it under the instance prefix.</summary>
    private static readonly string FreshKey = SourceCacheKey.For(SourceNames.Repositories, SearchFields.Text, SourceTestHost.Query);

    /// <summary>Fresh answers are not cached, so the second replica must call upstream and can only fall back to last-known-good items.</summary>
    private static readonly Dictionary<string, string?> Uncached = new() { [$"Sources:{SourceNames.Repositories}:Cache:Enabled"] = "false" };

    [Fact]
    public void AddInfrastructure_ShouldRegisterNoDistributedCache_WhenNoConnectionStringIsConfigured()
    {
        using var provider = TestServices.BuildInfrastructure([]);

        provider.GetService<IDistributedCache>().ShouldBeNull();
    }

    [Fact]
    public void AddInfrastructure_ShouldUseRedisAsTheSecondLevel_WhenTheCacheConnectionStringIsConfigured()
    {
        using var provider = TestServices.BuildInfrastructure(new() { [ConnectionStringKey] = "redis.test:6380,password=secret" });

        provider.GetServices<IDistributedCache>().ShouldHaveSingleItem().ShouldBeOfType<CircuitBreakingDistributedCache>();
        provider.GetService<RedisCache>().ShouldBeNull();
        var options = provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value;
        options.InstanceName.ShouldBe(SecondLevelCacheServiceCollectionExtensions.InstanceName);
        options.ConfigurationOptions.ShouldNotBeNull().EndPoints.Single().ToString().ShouldBe("Unspecified/redis.test:6380");
        options.ConfigurationOptions.Password.ShouldBe("secret");
    }

    [Fact]
    public void AddInfrastructure_ShouldPutRedisBehindTheBreaker_WhenTheHostRegisteredAnotherDistributedCacheFirst()
    {
        var services = TestServices.Create(new() { [ConnectionStringKey] = "localhost:6379" }, out var configuration);
        services.AddDistributedMemoryCache();

        services.AddInfrastructure(configuration);

        var redis = services.Where(descriptor => descriptor.ServiceType == typeof(IDistributedCache)).ToList();
        redis.ShouldHaveSingleItem().ImplementationFactory.ShouldNotBeNull();
    }

    [Fact]
    public void AddInfrastructure_ShouldFailFastWhileRedisIsDown_WhenTheCacheConnectionStringIsConfigured()
    {
        using var provider = TestServices.BuildInfrastructure(new() { [ConnectionStringKey] = "localhost:6379,abortConnect=true,connectTimeout=5000" });

        var redis = provider.GetRequiredService<IOptions<RedisCacheOptions>>().Value.ConfigurationOptions.ShouldNotBeNull();

        redis.AbortOnConnectFail.ShouldBeFalse();
        redis.BacklogPolicy.ShouldBe(BacklogPolicy.FailFast);
        redis.ConnectTimeout.ShouldBeLessThanOrEqualTo(SecondLevelCacheServiceCollectionExtensions.ConnectTimeoutMs);
        redis.AsyncTimeout.ShouldBeLessThanOrEqualTo(SecondLevelCacheServiceCollectionExtensions.OperationTimeoutMs);
        redis.SyncTimeout.ShouldBeLessThanOrEqualTo(SecondLevelCacheServiceCollectionExtensions.OperationTimeoutMs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost,unknownOption=1")]
    public async Task AddInfrastructure_ShouldFailAtStartup_WhenTheCacheConnectionStringIsBlankOrInvalid(string value)
    {
        using var provider = TestServices.BuildInfrastructure(new() { [ConnectionStringKey] = value });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() =>
            provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(ConnectionStringKey);
    }

    [Fact]
    public async Task AddInfrastructure_ShouldServeAnotherReplicaFromTheSharedSecondLevel_WhenOneReplicaFetchedFirst()
    {
        var shared = new SharedDistributedCache();
        using var http = Healthy();
        using var first = Replica(shared, http);
        using var second = Replica(shared, http);

        var fetched = await Fetch(first);
        var served = await Fetch(second);

        http.VerifyNoOutstandingExpectation();
        served.ShouldBeOfType<SourceCached>().Items.ShouldBe(fetched.ShouldBeOfType<SourceSuccess>().Items);
    }

    [Fact]
    public async Task AddInfrastructure_ShouldFetchAgain_WhenAnotherReplicaLeftAnUnreadableEntry()
    {
        var shared = new SharedDistributedCache();
        shared.Set(FreshKey, "{\"not\":\"items\"}"u8.ToArray(), new DistributedCacheEntryOptions());
        using var http = Healthy();
        using var provider = Replica(shared, http);

        var result = await Fetch(provider);

        http.VerifyNoOutstandingExpectation();
        result.ShouldBeOfType<SourceSuccess>().Items.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task AddInfrastructure_ShouldFallBackToAnotherReplicasLastKnownGood_WhenTheUpstreamFails()
    {
        var shared = new SharedDistributedCache();
        using var healthy = Healthy();
        using var failing = Failing();
        using var first = Replica(shared, healthy, Uncached);
        using var second = Replica(shared, failing, Uncached);

        var fetched = await Fetch(first);
        var fallback = await Fetch(second);

        fallback.ShouldBeOfType<SourceFallback>().Items.ShouldBe(fetched.ShouldBeOfType<SourceSuccess>().Items);
    }

    [Fact]
    public async Task AddInfrastructure_ShouldReportTheFailure_WhenTheSharedLastKnownGoodIsUnreadable()
    {
        var shared = new SharedDistributedCache();
        shared.Set(HybridLastKnownGoodStore.KeyPrefix + FreshKey, [0xFF, 0x00], new DistributedCacheEntryOptions());
        using var failing = Failing();
        using var provider = Replica(shared, failing, Uncached);

        var result = await Fetch(provider);

        result.ShouldBeOfType<SourceFailure>();
    }

    [Fact]
    public async Task AddInfrastructure_ShouldKeepServingFromMemory_WhenTheSecondLevelIsDown()
    {
        using var http = Healthy();
        var secondLevel = new UnreachableDistributedCache();
        using var provider = Replica(secondLevel, http);

        var fetched = await Fetch(provider);
        var cached = await Fetch(provider);

        http.VerifyNoOutstandingExpectation();
        fetched.ShouldBeOfType<SourceSuccess>();
        cached.ShouldBeOfType<SourceCached>();
        secondLevel.Calls.ShouldBePositive();
    }

    private static MockHttpMessageHandler Healthy()
    {
        var http = new MockHttpMessageHandler();
        http.Expect("*").Respond("application/json", SourceTestHost.Fixture("github-search.json"));
        return http;
    }

    private static MockHttpMessageHandler Failing()
    {
        var http = new MockHttpMessageHandler();
        http.When("*").Respond(HttpStatusCode.NotFound);
        return http;
    }

    private static ServiceProvider Replica(IDistributedCache secondLevel, MockHttpMessageHandler http, Dictionary<string, string?>? settings = null) =>
        TestServices.BuildInfrastructure(settings ?? [], services =>
        {
            services.AddSingleton(secondLevel);
            services.AddHttpClient(SourceNames.Repositories).ConfigurePrimaryHttpMessageHandler(() => http);
        });

    private static Task<SourceResult> Fetch(ServiceProvider provider) =>
        provider.GetServices<IDataSource>().Single(source => source.Name == SourceNames.Repositories)
            .FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

    /// <summary>A second level whose every call fails, as Redis does when it is unreachable.</summary>
    private sealed class UnreachableDistributedCache : IDistributedCache
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public byte[]? Get(string key) => throw Down();

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromException<byte[]?>(Down());

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Down();

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.FromException(Down());

        public void Refresh(string key) => throw Down();

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.FromException(Down());

        public void Remove(string key) => throw Down();

        public Task RemoveAsync(string key, CancellationToken token = default) => Task.FromException(Down());

        private InvalidOperationException Down()
        {
            Interlocked.Increment(ref calls);
            return new("The second level is unreachable.");
        }
    }
}
