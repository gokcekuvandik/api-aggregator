using Aggregator.Application.Statistics;
using Aggregator.Domain.Statistics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Statistics;

internal static class StatisticsServiceCollectionExtensions
{
    private const string BucketsSection = $"{StatisticsOptions.SectionName}:{nameof(StatisticsOptions.Buckets)}";
    private const string AnomaliesSection = $"{StatisticsOptions.SectionName}:{nameof(StatisticsOptions.Anomalies)}";

    /// <summary>A generous ceiling well inside the timer's own limit of about 49 days, so a typo fails validation instead of startup.</summary>
    private static readonly TimeSpan MaxAnomalyInterval = TimeSpan.FromDays(1);

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
            .Validate(options => AreValid(options.Anomalies), $"{AnomaliesSection}: Interval must be positive and at most a day, Threshold positive, and MinSamples at least 1.")
            .ValidateOnStart();
        services.AddMetrics();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<StatisticsOptions>>().Value.Thresholds);
        services.AddSingleton<StatisticsStore>();
        services.TryAddSingleton<IStatisticsReader>(provider => provider.GetRequiredService<StatisticsStore>());
        services.TryAddSingleton<ISourceTelemetry, SourceTelemetry>();
        services.AddHostedService<AnomalyDetector>();
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

    private static bool AreValid(StatisticsOptions.BucketOptions buckets) => Constructs(() => new PerformanceThresholds(buckets.FastBelow, buckets.SlowAbove));

    /// <summary>The rule's constructor holds the threshold and sample rules, so only the interval is checked here.</summary>
    private static bool AreValid(StatisticsOptions.AnomalyOptions anomalies) =>
        Constructs(() => anomalies.Rule) && anomalies.Interval > TimeSpan.Zero && anomalies.Interval <= MaxAnomalyInterval;

    /// <summary>Settings are valid when the domain type they build accepts them, so validation tries the constructor rather than restating its rules.</summary>
    private static bool Constructs(Func<object> create)
    {
        try
        {
            _ = create();
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
