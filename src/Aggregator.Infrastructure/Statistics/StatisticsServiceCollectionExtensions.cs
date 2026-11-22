using Aggregator.Application.Statistics;
using Aggregator.Domain.Statistics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Statistics;

internal static class StatisticsServiceCollectionExtensions
{
    private const string BucketsSection = $"{StatisticsOptions.SectionName}:{nameof(StatisticsOptions.Buckets)}";

    /// <summary>Registers the statistics store, read through <see cref="IStatisticsReader"/> and written only through <see cref="ISourceTelemetry"/>.</summary>
    public static IServiceCollection AddStatistics(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(StatisticsStore)))
        {
            return services;
        }

        services.AddOptions<StatisticsOptions>()
            .BindConfiguration(StatisticsOptions.SectionName)
            .Validate(options => AreValid(options.Buckets), $"{BucketsSection}: FastBelow must be positive and SlowAbove must not be shorter than it.")
            .ValidateOnStart();
        services.AddMetrics();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<StatisticsOptions>>().Value.Thresholds);
        services.AddSingleton<StatisticsStore>();
        services.TryAddSingleton<IStatisticsReader>(provider => provider.GetRequiredService<StatisticsStore>());
        services.TryAddSingleton<ISourceTelemetry, SourceTelemetry>();
        return services;
    }

    /// <summary>
    /// Adds the client's standard resilience pipeline between the two statistics handlers: one outside it, timing what the caller waits for, and one inside it, counting each attempt.
    /// The shared default pipeline has no client name, so it cannot read this client's tuned options, and it is replaced.
    /// </summary>
    public static IHttpClientBuilder AddMeasuredStandardResilience(this IHttpClientBuilder builder, UpstreamApi api)
    {
        builder.AddHttpMessageHandler(provider =>
            new StatisticsHandler(provider.GetRequiredService<ISourceTelemetry>(), api, provider.GetRequiredService<TimeProvider>()));

        // RemoveAllResilienceHandlers is the library's supported way to replace the default pipeline, though still marked experimental.
#pragma warning disable EXTEXP0001
        builder.RemoveAllResilienceHandlers().AddStandardResilienceHandler();
#pragma warning restore EXTEXP0001

        return builder.AddHttpMessageHandler(() => new AttemptCountingHandler());
    }

    /// <summary>The thresholds' own constructor holds the rules, so validation tries it rather than restating them.</summary>
    private static bool AreValid(StatisticsOptions.BucketOptions buckets)
    {
        try
        {
            _ = new PerformanceThresholds(buckets.FastBelow, buckets.SlowAbove);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
