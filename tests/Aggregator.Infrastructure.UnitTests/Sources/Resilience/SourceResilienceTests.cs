using Aggregator.Infrastructure.Sources;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
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
