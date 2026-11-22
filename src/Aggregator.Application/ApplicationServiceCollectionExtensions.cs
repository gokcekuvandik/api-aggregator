using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Aggregator.Application.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregator.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<AggregationTelemetry>();
        services.AddSingleton<DataSourceRegistry>();
        services.AddHostedService<DataSourceRegistryCheck>();
        services.AddSingleton<AggregationService>();
        return services;
    }
}
