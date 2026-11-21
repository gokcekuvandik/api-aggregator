using Aggregator.Infrastructure.Sources.Http;
using Aggregator.Infrastructure.Sources.News.HackerNews;
using Aggregator.Infrastructure.Sources.News.NewsApi;
using Aggregator.Infrastructure.Sources.Relevance;
using Aggregator.Infrastructure.Sources.Repositories.GitHub;
using Aggregator.Infrastructure.Sources.Weather.OpenMeteo;
using Aggregator.Infrastructure.Sources.Weather.OpenWeatherMap;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregator.Infrastructure.Sources;

/// <summary>
/// Registers one source per category. A configured API key makes the keyed provider primary, with the keyless one as its secondary;
/// without a key the keyless provider is the only one.
/// </summary>
internal static class SourceRegistration
{
    private const string ApiKeySetting = "ApiKey";

    public static IServiceCollection AddSources(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRelevanceScorer<object>, RankRelevanceScorer>();
        services.TryAddSingleton<IRelevanceScorer<RepositorySignal>, RepositoryRelevanceScorer>();
        services.TryAddSingleton<IRelevanceScorer<DateTimeOffset>, WeatherRelevanceScorer>();

        if (HasApiKey(configuration, SourceNames.Weather))
        {
            services.AddDataSource<OpenWeatherMapSource, OpenWeatherMapOptions>(SourceNames.Weather);
            services.AddSecondaryDataSource<OpenMeteoSource, OpenMeteoOptions>(SourceNames.Weather, ProviderNames.OpenMeteo);
        }
        else
        {
            services.AddDataSource<OpenMeteoSource, OpenMeteoOptions>(SourceNames.Weather);
        }

        if (HasApiKey(configuration, SourceNames.News))
        {
            services.AddDataSource<NewsApiSource, NewsApiOptions>(SourceNames.News);
            services.AddSecondaryDataSource<HackerNewsSource, HackerNewsOptions>(SourceNames.News, ProviderNames.HackerNews);
        }
        else
        {
            services.AddDataSource<HackerNewsSource, HackerNewsOptions>(SourceNames.News);
        }

        services.AddDataSource<GitHubSource, GitHubOptions>(SourceNames.Repositories).AddConditionalRequests();
        return services;
    }

    private static bool HasApiKey(IConfiguration configuration, string sourceName) =>
        !string.IsNullOrWhiteSpace(configuration[$"{DataSourceOptions.SectionFor(sourceName)}:{ApiKeySetting}"]);
}
