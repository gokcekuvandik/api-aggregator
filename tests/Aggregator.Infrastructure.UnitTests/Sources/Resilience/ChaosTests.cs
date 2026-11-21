using System.Diagnostics;
using Aggregator.Infrastructure.Sources;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using RichardSzalay.MockHttp;
using static Aggregator.Infrastructure.UnitTests.Sources.DataSourceRegistrationTests;

namespace Aggregator.Infrastructure.UnitTests.Sources.Resilience;

public sealed class ChaosTests : IDisposable
{
    private const string Name = "fake";
    private static readonly TimeSpan InjectedLatency = TimeSpan.FromMilliseconds(200);

    private readonly MockHttpMessageHandler _http = new();

    public ChaosTests() => _http.When("*").Respond("application/json", "[]");

    public void Dispose() => _http.Dispose();

    [Fact]
    public async Task Client_ShouldReachUpstream_WhenChaosIsNotConfigured()
    {
        using var provider = Build([]);

        using var response = await SendAsync(provider);

        response.IsSuccessStatusCode.ShouldBeTrue();
        _http.GetMatchCount(_http.Fallback).ShouldBe(0);
    }

    [Fact]
    public async Task Client_ShouldReachUpstream_WhenChaosIsDisabledDespiteRates()
    {
        using var provider = Build(new() { ["Sources:fake:Chaos:Enabled"] = "false", ["Sources:fake:Chaos:FaultRate"] = "1" });

        using var response = await SendAsync(provider);

        response.IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task Client_ShouldFailEveryAttemptWithTransportFault_WhenFaultRateIsOne()
    {
        using var provider = Build(new() { ["Sources:fake:Chaos:Enabled"] = "true", ["Sources:fake:Chaos:FaultRate"] = "1" });

        var exception = await Should.ThrowAsync<HttpRequestException>(() => SendAsync(provider));

        exception.Message.ShouldContain("Chaos");
    }

    [Fact]
    public async Task Client_ShouldDelayResponse_WhenLatencyRateIsOne()
    {
        using var provider = Build(new()
        {
            ["Sources:fake:Chaos:Enabled"] = "true",
            ["Sources:fake:Chaos:LatencyRate"] = "1",
            ["Sources:fake:Chaos:Latency"] = "00:00:00.2",
        });
        var started = Stopwatch.GetTimestamp();

        using var response = await SendAsync(provider);

        response.IsSuccessStatusCode.ShouldBeTrue();
        Stopwatch.GetElapsedTime(started).ShouldBeGreaterThanOrEqualTo(InjectedLatency - TimeSpan.FromMilliseconds(20));
    }

    [Theory]
    [InlineData("FaultRate", "1.5")]
    [InlineData("FaultRate", "-0.1")]
    [InlineData("LatencyRate", "2")]
    [InlineData("Latency", "-00:00:01")]
    [InlineData("Latency", "00:01:01")]
    public async Task AddDataSource_ShouldFailAtStartup_WhenChaosSettingIsOutOfRange(string setting, string value)
    {
        using var provider = Build(new() { [$"Sources:fake:Chaos:{setting}"] = value });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() =>
            provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain($"Sources:fake:Chaos:{setting}");
    }

    [Fact]
    public async Task AddDataSource_ShouldFailAtStartup_WhenChaosIsEnabledInProduction()
    {
        using var provider = Build(new() { ["Sources:fake:Chaos:Enabled"] = "true" }, Environments.Production);

        var exception = await Should.ThrowAsync<OptionsValidationException>(() =>
            provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("Sources:fake:Chaos:Enabled");
    }

    [Fact]
    public async Task Client_ShouldInjectFaults_WhenChaosIsEnabledInDevelopment()
    {
        using var provider = Build(new() { ["Sources:fake:Chaos:Enabled"] = "true", ["Sources:fake:Chaos:FaultRate"] = "1" }, Environments.Development);

        await provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(TestContext.Current.CancellationToken);

        await Should.ThrowAsync<HttpRequestException>(() => SendAsync(provider));
    }

    private static async Task<HttpResponseMessage> SendAsync(ServiceProvider provider)
    {
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(Name);
        return await client.GetAsync(new Uri("items", UriKind.Relative), TestContext.Current.CancellationToken);
    }

    private ServiceProvider Build(Dictionary<string, string?> settings, string? environment = null)
    {
        settings["Sources:fake:Resilience:Retry:Delay"] = "00:00:00.001";
        var services = TestServices.Create(settings);
        if (environment is not null)
        {
            var host = Substitute.For<IHostEnvironment>();
            host.EnvironmentName.Returns(environment);
            services.AddSingleton(host);
        }

        services.AddDataSource<FakeSource, FakeOptions>(Name).ConfigurePrimaryHttpMessageHandler(() => _http);
        return services.BuildServiceProvider();
    }
}
