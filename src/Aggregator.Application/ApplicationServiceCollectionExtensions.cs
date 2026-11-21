using Aggregator.Application.Aggregation;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregator.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<AggregationService>();
        return services;
    }
}
