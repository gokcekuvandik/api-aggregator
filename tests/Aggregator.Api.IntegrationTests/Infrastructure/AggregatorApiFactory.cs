using Aggregator.Application.Sources;
using Aggregator.Application.Statistics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>Hosts the API in memory and swaps the real sources for stubs.</summary>
public sealed class AggregatorApiFactory : WebApplicationFactory<Program>
{
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

    private HttpClient Create(Action<IServiceCollection> configure, string environment = "Development", CapturingLoggerProvider? logs = null) =>
        WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment).ConfigureTestServices(configure);
                if (logs is not null)
                {
                    builder.ConfigureLogging(logging => logging.AddProvider(logs));
                }
            })
            .CreateClient();

    private static void ReplaceSources(IServiceCollection services, IDataSource[] sources)
    {
        services.RemoveAll<IDataSource>();
        foreach (var source in sources)
        {
            services.AddSingleton(source);
        }
    }
}
