using Aggregator.Application.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregator.Infrastructure.Sources.Caching;

internal static class SourceCachingServiceCollectionExtensions
{
    /// <summary>
    /// Keys carry request text, so the in-memory cache is bounded and entries are sized by their serialized bytes.
    /// The limit applies to the shared IMemoryCache, so any other user of it must set a size on its entries.
    /// </summary>
    internal const long MemoryLimitBytes = 64L * 1024 * 1024;

    /// <summary>Registers <see cref="HybridCache"/> with an in-memory layer, the source cache and last-known-good store on it, and cache metrics.</summary>
    public static IServiceCollection AddSourceCaching(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ISourceCache)))
        {
            return services;
        }

        services.AddMemoryCache(options => options.SizeLimit = MemoryLimitBytes);
        services.AddHybridCache(options => options.MaximumPayloadBytes = DataSourceServiceCollectionExtensions.MaxResponseBytes)
            .AddSerializer<CachedItems, SourceItemsPayloadSerializer>();
        services.AddMetrics();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SourceCacheMetrics>();
        services.AddSingleton<ISourceCache, HybridSourceCache>();
        services.AddSingleton<ILastKnownGoodStore, HybridLastKnownGoodStore>();
        return services;
    }

    /// <summary>Binds <c>{section}:Cache</c> into options named after the source, over its default time to live, validated at startup.</summary>
    public static IServiceCollection AddSourceCacheOptions(this IServiceCollection services, string sourceName, string section)
    {
        var cacheSection = $"{section}:{SourceCacheOptions.SectionName}";
        services.AddOptions<SourceCacheOptions>(sourceName)
            .Configure(options => options.Ttl = SourceCacheOptions.DefaultTtlFor(sourceName))
            .BindConfiguration(cacheSection)
            .Validate(options => InRange(options.Ttl, SourceCacheOptions.MinTtl, SourceCacheOptions.MaxTtl),
                OptionsValidationMessages.OutOfRange(cacheSection, nameof(SourceCacheOptions.Ttl), SourceCacheOptions.MinTtl, SourceCacheOptions.MaxTtl))
            .Validate(options => InRange(options.LastKnownGoodTtl, SourceCacheOptions.MinLastKnownGoodTtl, SourceCacheOptions.MaxLastKnownGoodTtl),
                OptionsValidationMessages.OutOfRange(cacheSection, nameof(SourceCacheOptions.LastKnownGoodTtl), SourceCacheOptions.MinLastKnownGoodTtl, SourceCacheOptions.MaxLastKnownGoodTtl))
            .ValidateOnStart();
        return services;
    }

    private static bool InRange(TimeSpan value, TimeSpan min, TimeSpan max) => value >= min && value <= max;
}
