using System.Diagnostics.Metrics;
using Aggregator.Application.Telemetry;
using Aggregator.Infrastructure.Sources;
using Aggregator.Infrastructure.Sources.Resilience;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using static Aggregator.Infrastructure.UnitTests.Sources.DataSourceRegistrationTests;

namespace Aggregator.Infrastructure.UnitTests.Sources.Resilience;

public sealed class SourceResilienceTests
{
    private const string Name = "fake";

    [Fact]
    public void AddDataSource_ShouldTuneStandardPipeline_WhenNothingIsConfigured()
    {
        using var provider = Build([]);

        var options = Standard(provider, Name);

        options.AttemptTimeout.Timeout.ShouldBe(TimeSpan.FromSeconds(3));
        options.TotalRequestTimeout.Timeout.ShouldBe(TimeSpan.FromSeconds(8));
        options.Retry.MaxRetryAttempts.ShouldBe(2);
        options.Retry.UseJitter.ShouldBeTrue();
        options.CircuitBreaker.MinimumThroughput.ShouldBe(5);
        options.CircuitBreaker.FailureRatio.ShouldBe(0.5);
        options.CircuitBreaker.SamplingDuration.ShouldBe(TimeSpan.FromSeconds(30));
        options.CircuitBreaker.BreakDuration.ShouldBe(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void AddDataSource_ShouldBindResilienceSection_WhenConfigured()
    {
        using var provider = Build(new()
        {
            ["Sources:fake:Resilience:Retry:MaxRetryAttempts"] = "1",
            ["Sources:fake:Resilience:AttemptTimeout:Timeout"] = "00:00:01",
        });

        var options = Standard(provider, Name);

        options.Retry.MaxRetryAttempts.ShouldBe(1);
        options.AttemptTimeout.Timeout.ShouldBe(TimeSpan.FromSeconds(1));
        options.TotalRequestTimeout.Timeout.ShouldBe(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void AddSecondaryDataSource_ShouldTuneItsOwnClient_WhenSecondarySectionIsConfigured()
    {
        using var provider = Build(new() { ["Sources:fake:Secondary:Resilience:Retry:MaxRetryAttempts"] = "1" }, withSecondary: true);

        Standard(provider, "fake-secondary").Retry.MaxRetryAttempts.ShouldBe(1);
        Standard(provider, Name).Retry.MaxRetryAttempts.ShouldBe(2);
    }

    [Fact]
    public async Task AddDataSource_ShouldFailAtStartup_WhenTimeoutDoesNotExceedHttpTotalTimeout()
    {
        using var provider = Build(new() { ["Sources:fake:Timeout"] = "00:00:08" });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() =>
            provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("Sources:fake:Timeout");
        exception.Message.ShouldContain("Sources:fake:Resilience:TotalRequestTimeout");
    }

    [Fact]
    public async Task AddDataSource_ShouldPassStartup_WhenTotalTimeoutIsLoweredBelowTimeout()
    {
        using var provider = Build(new()
        {
            ["Sources:fake:Timeout"] = "00:00:05",
            ["Sources:fake:Resilience:TotalRequestTimeout:Timeout"] = "00:00:04",
        });

        await Should.NotThrowAsync(() => provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AddDataSource_ShouldReportEachProvidersCircuitState_WhenItsBreakerChangesState()
    {
        using var provider = Build([], withSecondary: true);
        using var states = new MetricCollector<int>(provider.GetRequiredService<IMeterFactory>(), AggregatorDiagnostics.Name, CircuitStateTracker.CircuitStateName);
        var primary = Standard(provider, Name).CircuitBreaker;
        var secondary = Standard(provider, "fake-secondary").CircuitBreaker;
        var context = ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        using var failed = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);

        await primary.OnOpened.ShouldNotBeNull()(new OnCircuitOpenedArguments<HttpResponseMessage>(context, Outcome.FromResult(failed), TimeSpan.FromSeconds(15), isManual: false));
        await secondary.OnHalfOpened.ShouldNotBeNull()(new OnCircuitHalfOpenedArguments(context));

        states.RecordObservableInstruments();
        var byProvider = states.GetMeasurementSnapshot().ToDictionary(measurement => (string)measurement.Tags["provider"]!, measurement => measurement.Value);
        byProvider.ShouldBe(new Dictionary<string, int> { ["primary"] = 2, ["fake-backup"] = 1 }, ignoreOrder: true);
        ResilienceContextPool.Shared.Return(context);
    }

    [Fact]
    public async Task AddDataSource_ShouldReportClosedAgain_WhenTheBreakerCloses()
    {
        using var provider = Build([]);
        using var states = new MetricCollector<int>(provider.GetRequiredService<IMeterFactory>(), AggregatorDiagnostics.Name, CircuitStateTracker.CircuitStateName);
        var breaker = Standard(provider, Name).CircuitBreaker;
        var context = ResilienceContextPool.Shared.Get(TestContext.Current.CancellationToken);
        using var failed = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
        using var recovered = new HttpResponseMessage(System.Net.HttpStatusCode.OK);

        await breaker.OnOpened.ShouldNotBeNull()(new OnCircuitOpenedArguments<HttpResponseMessage>(context, Outcome.FromResult(failed), TimeSpan.FromSeconds(15), isManual: false));
        await breaker.OnClosed.ShouldNotBeNull()(new OnCircuitClosedArguments<HttpResponseMessage>(context, Outcome.FromResult(recovered), isManual: false));

        states.RecordObservableInstruments();
        states.GetMeasurementSnapshot().ShouldHaveSingleItem().Value.ShouldBe(0);
        ResilienceContextPool.Shared.Return(context);
    }

    private static HttpStandardResilienceOptions Standard(ServiceProvider provider, string clientName) =>
        provider.GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>().Get($"{clientName}-standard");

    private static ServiceProvider Build(Dictionary<string, string?> settings, bool withSecondary = false)
    {
        var services = TestServices.Create(settings);
        services.AddDataSource<FakeSource, FakeOptions>(Name);
        if (withSecondary)
        {
            services.AddSecondaryDataSource<BackupSource, BackupOptions>(Name, "fake-backup");
        }

        return services.BuildServiceProvider();
    }
}
