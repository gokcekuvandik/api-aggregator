using Aggregator.Application.Sources;
using Aggregator.Infrastructure.Sources.Caching;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace Aggregator.Infrastructure.UnitTests.Sources.Caching;

public sealed class SourceCacheRegistrationTests
{
    [Theory]
    [InlineData("weather", 10)]
    [InlineData("news", 5)]
    [InlineData("repositories", 15)]
    public void AddInfrastructure_ShouldUseTheDefaultCacheSettings_WhenNothingIsConfigured(string source, int ttlMinutes)
    {
        using var provider = TestServices.BuildInfrastructure([]);

        var options = provider.GetRequiredService<IOptionsMonitor<SourceCacheOptions>>().Get(source);

        options.Enabled.ShouldBeTrue();
        options.Ttl.ShouldBe(TimeSpan.FromMinutes(ttlMinutes));
        options.LastKnownGoodTtl.ShouldBe(TimeSpan.FromHours(24));
    }

    [Fact]
    public void AddInfrastructure_ShouldBindCacheSettingsPerSource_WhenConfigured()
    {
        using var provider = TestServices.BuildInfrastructure(new()
        {
            ["Sources:news:Cache:Enabled"] = "false",
            ["Sources:news:Cache:Ttl"] = "00:01:00",
            ["Sources:news:Cache:LastKnownGoodTtl"] = "02:00:00",
        });
        var monitor = provider.GetRequiredService<IOptionsMonitor<SourceCacheOptions>>();

        var news = monitor.Get("news");

        news.Enabled.ShouldBeFalse();
        news.Ttl.ShouldBe(TimeSpan.FromMinutes(1));
        news.LastKnownGoodTtl.ShouldBe(TimeSpan.FromHours(2));
        monitor.Get("weather").Ttl.ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Theory]
    [InlineData("Ttl", "00:00:00")]
    [InlineData("Ttl", "01:00:01")]
    [InlineData("LastKnownGoodTtl", "00:00:59")]
    [InlineData("LastKnownGoodTtl", "7.00:00:01")]
    public async Task AddInfrastructure_ShouldFailAtStartup_WhenACacheLifetimeIsOutOfRange(string setting, string value)
    {
        using var provider = TestServices.BuildInfrastructure(new() { [$"Sources:weather:Cache:{setting}"] = value });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() =>
            provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain($"Sources:weather:Cache:{setting}");
    }

    [Fact]
    public void AddInfrastructure_ShouldLeaveTheSourceUncached_WhenCachingIsDisabled()
    {
        using var provider = TestServices.BuildInfrastructure(new() { ["Sources:news:Cache:Enabled"] = "false" });

        var sources = provider.GetServices<IDataSource>().ToDictionary(source => source.Name);

        sources["news"].ShouldBeOfType<ResilientDataSource>();
        sources["weather"].ShouldBeOfType<CachedDataSource>();
    }

    [Fact]
    public async Task AddInfrastructure_ShouldServeTheSecondCallFromTheCache_WhenCachingIsEnabled()
    {
        using var http = new MockHttpMessageHandler();
        http.Expect("*").Respond("application/json", SourceTestHost.Fixture("github-search.json"));
        using var provider = TestServices.BuildInfrastructure([], services => services.AddHttpClient("repositories").ConfigurePrimaryHttpMessageHandler(() => http));
        var source = provider.GetServices<IDataSource>().Single(s => s.Name == "repositories");

        var first = await source.FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);
        var second = await source.FetchAsync(SourceTestHost.Query with { Page = 2, City = "Athens" }, TestContext.Current.CancellationToken);

        http.VerifyNoOutstandingExpectation();
        second.ShouldBeOfType<SourceCached>().Items.ShouldBe(first.ShouldBeOfType<SourceSuccess>().Items);
    }

    [Fact]
    public async Task AddInfrastructure_ShouldRevalidateWithTheETag_WhenTheCachedEntryHasExpired()
    {
        using var http = new MockHttpMessageHandler();
        SourceTestHost.ExpectGitHubSearchThenNotModified(http, "\"v1\"");
        using var provider = TestServices.BuildInfrastructure(
            new() { ["Sources:repositories:Cache:Ttl"] = "00:00:01" },
            services => services.AddHttpClient("repositories").ConfigurePrimaryHttpMessageHandler(() => http));
        var source = provider.GetServices<IDataSource>().Single(s => s.Name == "repositories");
        var first = await source.FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(1300), TestContext.Current.CancellationToken);

        var renewed = await source.FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);
        var cached = await source.FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        http.VerifyNoOutstandingExpectation();
        renewed.ShouldBeOfType<SourceSuccess>().Items.ShouldBe(first.ShouldBeOfType<SourceSuccess>().Items);
        cached.ShouldBeOfType<SourceCached>();
    }
}
