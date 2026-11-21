using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregator.Application.Sources;

/// <summary>Builds the registry when the host starts, so duplicate or mismatched source names stop startup instead of the first request.</summary>
internal sealed class DataSourceRegistryCheck(IServiceProvider services) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        services.GetRequiredService<DataSourceRegistry>();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
