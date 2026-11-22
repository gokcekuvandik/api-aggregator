using System.Net.Http.Headers;
using Aggregator.Application.Sources;
using Aggregator.Application.Statistics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;

namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>Hosts the API in memory and swaps the real sources for stubs.</summary>
public sealed class AggregatorApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Valid for an hour, far longer than a test run.</summary>
    private static readonly string DefaultToken = TestTokens.Create(TestTokens.AllScopes);

    private WebApplicationFactory<Program>? _securedHost;

    public HttpClient CreateClientWith(params IDataSource[] sources) =>
        Create(services => ReplaceSources(services, sources));

    public HttpClient CreateClientWith(IStatisticsReader statistics) =>
        Create(services => services.Replace(ServiceDescriptor.Singleton(statistics)));

    public HttpClient CreateProductionClientWith(params IDataSource[] sources) =>
        CreateClientIn(Environments.Production, sources);

    /// <summary>Development throws on bad requests, while Production lets status code pages write the body.</summary>
    public HttpClient CreateClientIn(string environment, params IDataSource[] sources) =>
        Create(services => ReplaceSources(services, sources), environment);

    /// <summary>A Production client whose log entries land in <paramref name="logs"/>, with the app's own log filters applied.</summary>
    public HttpClient CreateClientWithLogs(CapturingLoggerProvider logs, params IDataSource[] sources) =>
        Create(services => ReplaceSources(services, sources), Environments.Production, logs);

    /// <summary>
    /// A Production client that keeps the real sources and applies <paramref name="settings"/>, typically base addresses of an <see cref="UpstreamServer"/>.
    /// Retries wait 1 ms instead of seconds so fault tests stay fast; a Retry-After header still sets the delay.
    /// </summary>
    public HttpClient CreateClientWithUpstreams(
        IReadOnlyDictionary<string, string?> settings,
        Action<IServiceCollection>? configure = null,
        CapturingLoggerProvider? logs = null) =>
        CreateWithUpstreams(settings, configure, logs).Client;

    /// <summary>Like <see cref="CreateClientWithUpstreams"/>, and also returns the host's services, such as its meter factory.</summary>
    public (HttpClient Client, IServiceProvider Services) CreateWithUpstreams(
        IReadOnlyDictionary<string, string?> settings,
        Action<IServiceCollection>? configure = null,
        CapturingLoggerProvider? logs = null)
    {
        var host = Host(
            services =>
            {
                services.ConfigureAll<HttpStandardResilienceOptions>(options =>
                {
                    options.Retry.Delay = TimeSpan.FromMilliseconds(1);
                    options.Retry.BackoffType = DelayBackoffType.Constant;
                    options.Retry.UseJitter = false;
                });
                configure?.Invoke(services);
            },
            Environments.Production,
            logs,
            settings);
        return (host.CreateClient(), host.Services);
    }

    /// <summary>One Production host with a stub news source, shared by tests that vary only the token or the route.</summary>
    public WebApplicationFactory<Program> SecuredHost => _securedHost ??= HostWith(sources: StubDataSource.InState("news", "Success"));

    /// <summary>
    /// A Production host with <paramref name="settings"/> applied on top of the test defaults, and <paramref name="sources"/> in place of the real ones when given.
    /// Several clients of one host share its rate limiter; <see cref="TokenClients.CreateClientWithToken"/> picks each client's token.
    /// </summary>
    public WebApplicationFactory<Program> HostWith(IReadOnlyDictionary<string, string?>? settings = null, params IDataSource[] sources) =>
        Host(
            services =>
            {
                if (sources.Length > 0)
                {
                    ReplaceSources(services, sources);
                }
            },
            Environments.Production,
            logs: null,
            settings);

    /// <summary>Every host trusts the test signing key and allows far more requests than any test sends, unless a test overrides it.</summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in DefaultSettings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
            services.ConfigureHttpClientDefaults(http => http.AddHttpMessageHandler(() => new LoopbackOnlyHandler())));
    }

    /// <summary>Every client signs in with both scopes unless a test replaces the header.</summary>
    protected override void ConfigureClient(HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme, DefaultToken);
    }

    private HttpClient Create(
        Action<IServiceCollection> configure,
        string environment = "Development",
        CapturingLoggerProvider? logs = null,
        IReadOnlyDictionary<string, string?>? settings = null) =>
        Host(configure, environment, logs, settings).CreateClient();

    private WebApplicationFactory<Program> Host(
        Action<IServiceCollection> configure,
        string environment,
        CapturingLoggerProvider? logs,
        IReadOnlyDictionary<string, string?>? settings) =>
        WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment).ConfigureTestServices(configure);
                foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
                {
                    builder.UseSetting(key, value);
                }

                if (logs is not null)
                {
                    builder.ConfigureLogging(logging => logging.AddProvider(logs));
                }
            });

    private static IEnumerable<KeyValuePair<string, string?>> DefaultSettings =>
        TestTokens.Settings.Append(new("RateLimiting:TokenLimit", "100000"));

    private static void ReplaceSources(IServiceCollection services, IDataSource[] sources)
    {
        services.RemoveAll<IDataSource>();
        foreach (var source in sources)
        {
            services.AddSingleton(source);
        }
    }
}
