using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aggregator.Infrastructure.UnitTests.TestDoubles;

/// <summary>A service collection with in-memory configuration and logging, the base every registration test builds on.</summary>
internal static class TestServices
{
    public static ServiceCollection Create(Dictionary<string, string?> settings) => Create(settings, out _);

    public static ServiceCollection Create(Dictionary<string, string?> settings, out IConfiguration configuration)
    {
        configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddLogging();
        return services;
    }

    /// <summary>Builds a provider with everything <c>AddInfrastructure</c> registers, then any test overrides.</summary>
    public static ServiceProvider BuildInfrastructure(Dictionary<string, string?> settings, Action<IServiceCollection>? configure = null)
    {
        var services = Create(settings, out var configuration);
        services.AddInfrastructure(configuration);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
