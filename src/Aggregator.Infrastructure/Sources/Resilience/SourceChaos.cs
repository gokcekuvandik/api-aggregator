using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Polly.Simmy;
using Polly.Simmy.Fault;
using Polly.Simmy.Latency;

namespace Aggregator.Infrastructure.Sources.Resilience;

/// <summary>
/// Adds Polly's chaos strategies inside the standard pipeline, so retries, timeouts and the circuit breaker react to them as to real faults.
/// The handler is always present; the configuration switches it on, which keeps demos a settings change away.
/// Startup fails if it is switched on in Production.
/// </summary>
internal static class SourceChaos
{
    private const string SectionName = "Chaos";
    internal const string FaultMessage = "Chaos fault injected.";

    private const string PipelineName = "chaos";
    private static readonly TimeSpan MaxLatency = TimeSpan.FromMinutes(1);

    public static IHttpClientBuilder AddSourceChaos(this IHttpClientBuilder builder, string section)
    {
        var chaosSection = $"{section}:{SectionName}";
        var clientName = builder.Name;
        builder.Services.AddOptions<ChaosOptions>(clientName).BindConfiguration(chaosSection).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ChaosOptions>>(provider =>
            new ChaosOptionsValidator(clientName, chaosSection, provider.GetService<IHostEnvironment>()?.IsProduction() == true));

        builder.AddResilienceHandler(PipelineName, (pipeline, context) =>
        {
            var options = context.ServiceProvider.GetRequiredService<IOptionsMonitor<ChaosOptions>>().Get(clientName);
            pipeline
                .AddChaosLatency(new ChaosLatencyStrategyOptions
                {
                    Enabled = options.Enabled && options.LatencyRate > 0,
                    InjectionRate = options.LatencyRate,
                    Latency = options.Latency,
                })
                .AddChaosFault(new ChaosFaultStrategyOptions
                {
                    Enabled = options.Enabled && options.FaultRate > 0,
                    InjectionRate = options.FaultRate,
                    FaultGenerator = static _ => ValueTask.FromResult<Exception?>(new HttpRequestException(FaultMessage)),
                });
        });

        return builder;
    }

    private sealed class ChaosOptionsValidator(string? optionsName, string section, bool isProduction) : IValidateOptions<ChaosOptions>
    {
        public ValidateOptionsResult Validate(string? name, ChaosOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (!string.Equals(name, optionsName, StringComparison.Ordinal))
            {
                return ValidateOptionsResult.Skip;
            }

            List<string> failures = [];
            if (options.Enabled && isProduction)
            {
                failures.Add(OptionsValidationMessages.ChaosInProduction(section));
            }

            if (options.FaultRate is < 0 or > 1)
            {
                failures.Add(OptionsValidationMessages.NotARate(section, nameof(ChaosOptions.FaultRate)));
            }

            if (options.LatencyRate is < 0 or > 1)
            {
                failures.Add(OptionsValidationMessages.NotARate(section, nameof(ChaosOptions.LatencyRate)));
            }

            if (options.Latency < TimeSpan.Zero || options.Latency > MaxLatency)
            {
                failures.Add(OptionsValidationMessages.LatencyOutOfRange(section, MaxLatency));
            }

            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }
    }
}
