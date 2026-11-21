using Aggregator.Application.Statistics;
using Aggregator.Infrastructure.Statistics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aggregator.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<IStatisticsReader, EmptyStatisticsReader>();
        return services;
    }
}
