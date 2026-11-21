using Aggregator.Application.Sources;
using Aggregator.Application.Statistics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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

    /// <summary>
    /// Error paths differ by environment: Development throws on bad requests (ThrowOnBadRequest),
    /// Production sets the status and lets UseStatusCodePages write the body.
    /// </summary>
    public HttpClient CreateClientIn(string environment, params IDataSource[] sources) =>
        Create(services => ReplaceSources(services, sources), environment);

    private HttpClient Create(Action<IServiceCollection> configure, string environment = "Development") =>
        WithWebHostBuilder(builder => builder.UseEnvironment(environment).ConfigureTestServices(configure))
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
