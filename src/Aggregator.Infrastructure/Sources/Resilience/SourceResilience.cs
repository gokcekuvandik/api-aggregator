using Aggregator.Application.Statistics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.Sources.Resilience;

/// <summary>
/// Tunes the standard resilience pipeline of one provider's client, then applies <c>{section}:Resilience</c>.
/// The library defaults open the circuit only after 100 calls in 30 seconds, which this API rarely reaches.
/// </summary>
internal static class SourceResilience
{
    private const string SectionName = "Resilience";

    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan SamplingDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BreakDuration = TimeSpan.FromSeconds(15);
    private const int MaxRetryAttempts = 2;
    private const int MinimumThroughput = 5;
    private const double FailureRatio = 0.5;

    /// <summary>The options name <c>AddStandardResilienceHandler</c> uses for a client.</summary>
    private static string OptionsName(string clientName) => $"{clientName}-standard";

    public static void AddSourceResilience<TOptions>(this IServiceCollection services, UpstreamApi api, string section, string clientName)
        where TOptions : DataSourceOptions
    {
        services.TryAddSingleton<CircuitStateTracker>();
        services.AddSingleton(new TrackedCircuit(api));

        // Post-configured so binding the section cannot replace the callbacks.
        services.AddOptions<HttpStandardResilienceOptions>(OptionsName(clientName))
            .Configure(ApplyDefaults)
            .BindConfiguration(ResilienceSection(section))
            .PostConfigure<CircuitStateTracker>((options, tracker) => ReportCircuitState(options.CircuitBreaker, tracker, api));
        services.AddSingleton<IValidateOptions<TOptions>>(provider => new TimeoutBudgetValidator<TOptions>(
            section, clientName, provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()));
    }

    private static string ResilienceSection(string section) => $"{section}:{SectionName}";

    /// <summary>Reports the breaker's transitions to the tracker; nothing else sets these callbacks.</summary>
    private static void ReportCircuitState(HttpCircuitBreakerStrategyOptions breaker, CircuitStateTracker tracker, UpstreamApi api)
    {
        breaker.OnOpened = _ =>
        {
            tracker.Opened(api);
            return ValueTask.CompletedTask;
        };
        breaker.OnHalfOpened = _ =>
        {
            tracker.HalfOpened(api);
            return ValueTask.CompletedTask;
        };
        breaker.OnClosed = _ =>
        {
            tracker.Closed(api);
            return ValueTask.CompletedTask;
        };
    }

    private static void ApplyDefaults(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = AttemptTimeout;
        options.TotalRequestTimeout.Timeout = TotalTimeout;
        options.Retry.MaxRetryAttempts = MaxRetryAttempts;
        options.CircuitBreaker.MinimumThroughput = MinimumThroughput;
        options.CircuitBreaker.FailureRatio = FailureRatio;
        options.CircuitBreaker.SamplingDuration = SamplingDuration;
        options.CircuitBreaker.BreakDuration = BreakDuration;
    }

    /// <summary>A provider's timeout must outlast its HTTP pipeline, so the pipeline's own failure is what gets reported.</summary>
    private sealed class TimeoutBudgetValidator<TOptions>(
        string section, string clientName, IOptionsMonitor<HttpStandardResilienceOptions> resilience) : IValidateOptions<TOptions>
        where TOptions : DataSourceOptions
    {
        public ValidateOptionsResult Validate(string? name, TOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var total = resilience.Get(OptionsName(clientName)).TotalRequestTimeout.Timeout;
            return options.Timeout > total
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    $"{section}:{nameof(DataSourceOptions.Timeout)} ({options.Timeout}) must be longer than " +
                    $"{ResilienceSection(section)}:TotalRequestTimeout:Timeout ({total}).");
        }
    }
}
