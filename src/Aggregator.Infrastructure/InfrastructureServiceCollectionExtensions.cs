using Aggregator.Application.Statistics;
using Aggregator.Infrastructure.Sources;
using Aggregator.Infrastructure.Statistics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregator.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IStatisticsReader, EmptyStatisticsReader>();
        services.AddSources(configuration);
        return services;
    }
}
