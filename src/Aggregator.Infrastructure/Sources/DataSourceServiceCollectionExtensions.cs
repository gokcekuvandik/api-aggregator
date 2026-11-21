using System.Net.Http.Headers;
using Aggregator.Application.Sources;
using Aggregator.Infrastructure.Sources.Caching;
using Aggregator.Infrastructure.Sources.Http;
using Aggregator.Infrastructure.Sources.Resilience;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Sources;

public static class DataSourceServiceCollectionExtensions
{
    private const string UserAgentProduct = "AgileActorsAggregator";
    private const string UserAgentVersion = "1.0";

    /// <summary>Upstream payloads are a few hundred kilobytes; anything far larger is refused rather than buffered.</summary>
    internal const int MaxResponseBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Registers a source's primary provider with its options (bound from <c>Sources:{name}</c>, validated at startup) and its named HTTP client.
    /// The source is exposed as a <see cref="CachedDataSource"/>, unless its cache is disabled, over a <see cref="ResilientDataSource"/>, which falls back to a secondary provider, if one is registered, and to last-known-good items.
    /// Cache settings come from <c>Sources:{name}:Cache</c>.
    /// The returned builder adds handlers that only this provider needs.
    /// </summary>
    public static IHttpClientBuilder AddDataSource<TSource, TOptions>(this IServiceCollection services, string name)
        where TSource : class, IDataSource
        where TOptions : DataSourceOptions, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSourceCaching();
        services.AddSourceCacheOptions(name, DataSourceOptions.SectionFor(name));
        services.AddSingleton<IDataSource>(provider =>
        {
            var cache = provider.GetRequiredService<IOptionsMonitor<SourceCacheOptions>>().Get(name);
            var time = provider.GetRequiredService<TimeProvider>();
            var resilient = new ResilientDataSource(
                Provider<TSource, TOptions>(provider, name, name),
                provider.GetKeyedService<SourceProvider>(SecondaryClientName(name)),
                provider.GetRequiredService<ILastKnownGoodStore>(),
                time,
                cache.LastKnownGoodTtl);
            return cache.Enabled
                ? new CachedDataSource(resilient, provider.GetRequiredService<ISourceCache>(), cache.Ttl, time, provider.GetRequiredService<SourceCacheMetrics>())
                : resilient;
        });

        return services.AddProvider<TSource, TOptions>(name, DataSourceOptions.SectionFor(name), clientName: name);
    }

    /// <summary>
    /// Registers the provider a source falls back to when its primary fails, bound from <c>Sources:{name}:Secondary</c>
    /// and sending on its own client, <c>{name}-secondary</c>, so its retries and circuit breaker are separate.
    /// <paramref name="providerName"/> is reported to clients when it serves the data.
    /// </summary>
    public static IHttpClientBuilder AddSecondaryDataSource<TSource, TOptions>(this IServiceCollection services, string name, string providerName)
        where TSource : class, IDataSource
        where TOptions : DataSourceOptions, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);

        services.AddKeyedSingleton(SecondaryClientName(name), (provider, _) => Provider<TSource, TOptions>(provider, name, providerName));
        return services.AddProvider<TSource, TOptions>(name, DataSourceOptions.SecondarySectionFor(name), SecondaryClientName(name));
    }

    private static IHttpClientBuilder AddProvider<TSource, TOptions>(this IServiceCollection services, string name, string section, string clientName)
        where TSource : class, IDataSource
        where TOptions : DataSourceOptions, new()
    {
        // Options and the adapter are registered once per type, so one type cannot serve two roles.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(TSource)))
        {
            throw new InvalidOperationException($"{typeof(TSource).Name} is already registered as a provider; each provider type can be registered only once.");
        }

        services.AddOptions<TOptions>().BindConfiguration(section).Configure(options => options.ClientName = clientName).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TOptions>>(new DataSourceOptionsValidator<TOptions>(section)));
        services.AddSourceResilience<TOptions>(section, clientName);

        services.TryAddSingleton<SourceFetcher>();
        services.AddSingleton<TSource>();

        var builder = services.AddHttpClient(clientName, (provider, client) =>
        {
            client.BaseAddress = provider.GetRequiredService<IOptions<TOptions>>().Value.BaseAddress;
            client.MaxResponseContentBufferSize = MaxResponseBytes;
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgentProduct, UserAgentVersion));
        });

        // The shared default pipeline has no client name, so it cannot read this client's tuned options; a named one replaces it.
        // RemoveAllResilienceHandlers is the library's supported way to do that, though still marked experimental.
#pragma warning disable EXTEXP0001
        builder.RemoveAllResilienceHandlers().AddStandardResilienceHandler();
#pragma warning restore EXTEXP0001
        builder.AddSourceChaos(section);

        return builder.AddHttpMessageHandler(provider => new RateLimitLoggingHandler(provider.GetRequiredService<ILogger<RateLimitLoggingHandler>>(), name));
    }

    private static SourceProvider Provider<TSource, TOptions>(IServiceProvider provider, string name, string providerName)
        where TSource : class, IDataSource
        where TOptions : DataSourceOptions, new() =>
        new(providerName, EnsureNamed(provider.GetRequiredService<TSource>(), name), provider.GetRequiredService<IOptions<TOptions>>().Value.Timeout);

    private static string SecondaryClientName(string sourceName) => $"{sourceName}-secondary";

    private static TSource EnsureNamed<TSource>(TSource source, string name)
        where TSource : IDataSource =>
        string.Equals(source.Name, name, StringComparison.Ordinal)
            ? source
            : throw new InvalidOperationException(
                $"{typeof(TSource).Name} is registered as '{name}' but reports the name '{source.Name}'. Use the same name in both places.");
}
