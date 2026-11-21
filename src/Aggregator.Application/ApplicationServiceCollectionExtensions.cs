using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregator.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<DataSourceRegistry>();
        services.AddHostedService<DataSourceRegistryCheck>();
        services.AddSingleton<AggregationService>();
        return services;
    }
}
