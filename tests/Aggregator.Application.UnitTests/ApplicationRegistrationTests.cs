using System.Diagnostics.Metrics;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Aggregator.Application.UnitTests.TestData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aggregator.Application.UnitTests;

public sealed class ApplicationRegistrationTests
{
    [Fact]
    public async Task AddApplication_ShouldResolveTheAggregationService_WhenSourcesAreRegistered()
    {
        await using var provider = Provider(TestSources.Returning("news", new SourceSuccess([])));

        var result = await provider.GetRequiredService<AggregationService>()
            .AggregateAsync(new AggregationQuery(Page: 1, PageSize: 10), TestContext.Current.CancellationToken);

        result.Sources.ShouldHaveSingleItem().Name.ShouldBe("news");
    }

    [Fact]
    public async Task AddApplication_ShouldStopStartup_WhenTwoSourcesShareAName()
    {
        await using var provider = Provider(TestSources.Returning("news", new SourceSuccess([])), TestSources.Returning("News", new SourceSuccess([])));

        var startup = provider.GetServices<IHostedService>().ShouldHaveSingleItem();

        await Should.ThrowAsync<InvalidOperationException>(() => startup.StartAsync(TestContext.Current.CancellationToken));
        await startup.StopAsync(TestContext.Current.CancellationToken);
    }

    private static ServiceProvider Provider(params IDataSource[] sources)
    {
        var services = new ServiceCollection().AddSingleton<IMeterFactory, TestMeterFactory>();
        foreach (var source in sources)
        {
            services.AddSingleton(source);
        }

        return services.AddApplication().BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
