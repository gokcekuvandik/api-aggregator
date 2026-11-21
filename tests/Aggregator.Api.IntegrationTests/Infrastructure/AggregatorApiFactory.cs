using Aggregator.Application.Sources;
using Aggregator.Application.Statistics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregator.Api.IntegrationTests.Infrastructure;

/// <summary>Hosts the API in memory and swaps the real sources for stubs.</summary>
public sealed class AggregatorApiFactory : WebApplicationFactory<Program>
{
    public HttpClient CreateClientWith(params IDataSource[] sources) =>
        Configure(services => ReplaceSources(services, sources)).CreateClient();

    public HttpClient CreateClientWith(IStatisticsReader statistics) =>
        Configure(services => services.Replace(ServiceDescriptor.Singleton(statistics))).CreateClient();

    public HttpClient CreateProductionClientWith(params IDataSource[] sources) =>
        WithWebHostBuilder(builder => builder
                .UseEnvironment("Production")
                .ConfigureTestServices(services => ReplaceSources(services, sources)))
            .CreateClient();

    private WebApplicationFactory<Program> Configure(Action<IServiceCollection> configure) =>
        WithWebHostBuilder(builder => builder.ConfigureTestServices(configure));

    private static void ReplaceSources(IServiceCollection services, IDataSource[] sources)
    {
        services.RemoveAll<IDataSource>();
        foreach (var source in sources)
        {
            services.AddSingleton(source);
        }
    }
}
