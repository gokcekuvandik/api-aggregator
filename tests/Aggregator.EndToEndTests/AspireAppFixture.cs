using System.Net.Http.Headers;
using Aggregator.Api.IntegrationTests.Infrastructure;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace Aggregator.EndToEndTests;

/// <summary>
/// Starts the real AppHost once per test class, with every source pointed at a local WireMock server and a test signing key, so nothing leaves the machine.
/// </summary>
public sealed class AspireAppFixture : IAsyncLifetime
{
    private const string ApiResource = "api";
    private const string ApiEndpoint = "http";
    private const string BreakerThroughputNoRunReaches = "1000";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(3);

    private DistributedApplication? _app;

    public UpstreamServer Upstream { get; } = new();

    public HttpClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        try
        {
            await StartAsync();
        }
        catch
        {
            // xUnit does not dispose a fixture whose initialisation failed, so the API process and WireMock are stopped here.
            await DisposeAsync();
            throw;
        }
    }

    private async Task StartAsync()
    {
        await Upstream.InitializeAsync();
        Upstream.StubKeylessSources();

        using var startup = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        startup.CancelAfter(StartupTimeout);
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Aggregator_AppHost>(
            [],
            (options, _) =>
            {
                options.DisableDashboard = true;
                options.AllowUnsecuredTransport = true;
            },
            startup.Token);

        var api = builder.CreateResourceBuilder<IResourceWithEnvironment>(ApiResource);
        foreach (var (key, value) in ApiSettings())
        {
            api.WithEnvironment(key.Replace(":", "__", StringComparison.Ordinal), value ?? string.Empty);
        }

        _app = await builder.BuildAsync(startup.Token);
        await _app.StartAsync(startup.Token);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync(ApiResource, startup.Token);

        Client = _app.CreateHttpClient(ApiResource, ApiEndpoint);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(TestTokens.AllScopes));
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        await Upstream.DisposeAsync();
    }

    /// <summary>
    /// The upstream addresses and test key, with the source cache off so every request reaches the upstream.
    /// The tests share one API process, so no circuit breaker may open on what one test's faults leave behind.
    /// </summary>
    private Dictionary<string, string?> ApiSettings()
    {
        var settings = Upstream.SettingsWithoutCache();
        foreach (var (key, value) in TestTokens.Settings)
        {
            settings[key] = value;
        }

        settings["RateLimiting:TokenLimit"] = "100000";
        foreach (var (source, _) in UpstreamServer.Upstreams)
        {
            settings[$"Sources:{source}:Resilience:CircuitBreaker:MinimumThroughput"] = BreakerThroughputNoRunReaches;
        }

        return settings;
    }
}
