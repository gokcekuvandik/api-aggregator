using Aggregator.Application;
using Aggregator.Application.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Aggregator.Infrastructure.UnitTests.Sources;

public sealed class DataSourceRegistryCheckTests
{
    [Fact]
    public async Task StartAsync_ShouldThrow_WhenTwoSourcesShareAName()
    {
        using var provider = Build(Source("news"), Source("News"));

        await Should.ThrowAsync<InvalidOperationException>(() => StartAllAsync(provider));
    }

    [Fact]
    public async Task StartAsync_ShouldSucceed_WhenNamesAreUnique()
    {
        using var provider = Build(Source("news"), Source("weather"));

        await Should.NotThrowAsync(() => StartAllAsync(provider));
    }

    private static async Task StartAllAsync(ServiceProvider provider)
    {
        var hostedServices = provider.GetServices<IHostedService>().ToList();
        hostedServices.ShouldNotBeEmpty();
        foreach (var service in hostedServices)
        {
            await service.StartAsync(TestContext.Current.CancellationToken);
        }
    }

    private static ServiceProvider Build(params IDataSource[] sources)
    {
        var services = new ServiceCollection();
        services.AddApplication();
        foreach (var source in sources)
        {
            services.AddSingleton(source);
        }

        return services.BuildServiceProvider();
    }

    private static IDataSource Source(string name)
    {
        var source = Substitute.For<IDataSource>();
        source.Name.Returns(name);
        return source;
    }
}
