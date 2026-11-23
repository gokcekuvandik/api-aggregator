using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Aggregator.Infrastructure.Sources.Caching;

internal static class SecondLevelCacheServiceCollectionExtensions
{
    /// <summary>The connection string name Aspire's <c>WithReference</c> writes for the Redis resource.</summary>
    internal const string ConnectionStringName = "cache";

    internal const string InstanceName = "aggregator:";

    /// <summary>Redis is a shared cache, not a source of truth, so a slow or missing server must cost a request milliseconds, not seconds.</summary>
    internal const int ConnectTimeoutMs = 1000;

    internal const int OperationTimeoutMs = 500;

    /// <summary>
    /// Adds Redis as the HybridCache second level when <c>ConnectionStrings:cache</c> is set, so replicas share cached answers and last-known-good items.
    /// While Redis is unreachable its calls fail at once and HybridCache answers from memory.
    /// Without the connection string the cache stays in memory per instance.
    /// </summary>
    public static IServiceCollection AddSourceCacheSecondLevel(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (connectionString is null)
        {
            return services;
        }

        services.AddStackExchangeRedisCache(options =>
        {
            options.InstanceName = InstanceName;
            options.ConfigurationOptions = FailFast(connectionString);
        });
        // The library's own registration is moved behind the breaker, so its logging is kept and no other cache can shadow Redis.
        var redis = services.Last(descriptor => descriptor.ServiceType == typeof(IDistributedCache) && descriptor.ImplementationType?.Assembly == typeof(RedisCacheOptions).Assembly);
        services.RemoveAll<IDistributedCache>();
        services.AddSingleton<IDistributedCache>(provider => new CircuitBreakingDistributedCache(
            (IDistributedCache)ActivatorUtilities.CreateInstance(provider, redis.ImplementationType!),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<CircuitBreakingDistributedCache>>()));
        services.AddOptions<RedisCacheOptions>()
            .Validate(options => options.ConfigurationOptions is not null, OptionsValidationMessages.InvalidRedisConnection(ConnectionStringName))
            .ValidateOnStart();
        return services;
    }

    private static ConfigurationOptions? FailFast(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        ConfigurationOptions options;
        try
        {
            options = ConfigurationOptions.Parse(connectionString);
        }
        catch (ArgumentException)
        {
            return null;
        }

        options.AbortOnConnectFail = false;
        options.BacklogPolicy = BacklogPolicy.FailFast;
        options.ConnectTimeout = Math.Min(options.ConnectTimeout, ConnectTimeoutMs);
        options.AsyncTimeout = Math.Min(options.AsyncTimeout, OperationTimeoutMs);
        options.SyncTimeout = Math.Min(options.SyncTimeout, OperationTimeoutMs);
        return options;
    }
}
