using Aggregator.Infrastructure.Sources;
using Aggregator.Infrastructure.Statistics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregator.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddStatistics();
        services.AddSources(configuration);
        return services;
    }
}
