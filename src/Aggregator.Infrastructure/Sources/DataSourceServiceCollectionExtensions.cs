using System.Net.Http.Headers;
using Aggregator.Application.Sources;
using Aggregator.Infrastructure.Sources.Http;
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
    /// Registers a source with its options (bound from <c>Sources:{name}</c>, validated at startup) and its named HTTP client.
    /// The returned builder adds handlers that only this source needs.
    /// </summary>
    public static IHttpClientBuilder AddDataSource<TSource, TOptions>(this IServiceCollection services, string name)
        where TSource : class, IDataSource
        where TOptions : DataSourceOptions, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var section = DataSourceOptions.SectionFor(name);
        services.AddOptions<TOptions>().BindConfiguration(section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TOptions>>(new DataSourceOptionsValidator<TOptions>(section)));

        services.TryAddSingleton<SourceFetcher>();
        services.AddSingleton<TSource>();
        services.AddSingleton<IDataSource>(provider => EnsureNamed(provider.GetRequiredService<TSource>(), name));

        return services
            .AddHttpClient(name, (provider, client) =>
            {
                client.BaseAddress = provider.GetRequiredService<IOptions<TOptions>>().Value.BaseAddress;
                client.MaxResponseContentBufferSize = MaxResponseBytes;
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(UserAgentProduct, UserAgentVersion));
            })
            .AddHttpMessageHandler(provider => new RateLimitLoggingHandler(provider.GetRequiredService<ILogger<RateLimitLoggingHandler>>(), name));
    }

    private static TSource EnsureNamed<TSource>(TSource source, string name)
        where TSource : IDataSource =>
        string.Equals(source.Name, name, StringComparison.Ordinal)
            ? source
            : throw new InvalidOperationException(
                $"{typeof(TSource).Name} is registered as '{name}' but reports the name '{source.Name}'. Use the same name in both places.");
}
