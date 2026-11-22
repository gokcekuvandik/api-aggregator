using Aggregator.Application.Sources;
using Aggregator.Infrastructure.Sources.News.HackerNews;
using Aggregator.Infrastructure.Sources.News.NewsApi;
using Aggregator.Infrastructure.Sources.Repositories.GitHub;
using Aggregator.Infrastructure.Sources.Weather.OpenMeteo;
using Aggregator.Infrastructure.Sources.Weather.OpenWeatherMap;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace Aggregator.Infrastructure.UnitTests;

public sealed class InfrastructureRegistrationTests
{
    [Fact]
    public void AddInfrastructure_ShouldRegisterKeylessProviders_WhenNoKeyIsConfigured()
    {
        using var provider = TestServices.BuildInfrastructure([]);

        var sources = Composed(provider);

        sources.Select(s => s.Primary.Source.GetType()).ShouldBe([typeof(OpenMeteoSource), typeof(HackerNewsSource), typeof(GitHubSource)]);
        sources.ShouldAllBe(s => s.Secondary == null);
    }

    [Fact]
    public void AddInfrastructure_ShouldRegisterKeyedProvidersWithKeylessSecondaries_WhenKeysAreConfigured()
    {
        using var provider = TestServices.BuildInfrastructure(new() { ["Sources:weather:ApiKey"] = "w", ["Sources:news:ApiKey"] = "n" });

        var sources = Composed(provider);

        sources.Select(s => s.Primary.Source.GetType()).ShouldBe([typeof(OpenWeatherMapSource), typeof(NewsApiSource), typeof(GitHubSource)]);
        sources.Select(s => s.Secondary?.Source.GetType()).ShouldBe([typeof(OpenMeteoSource), typeof(HackerNewsSource), null]);
        sources.Select(s => s.Secondary?.Name).ShouldBe(["open-meteo", "hacker-news", null]);
    }

    [Fact]
    public void AddInfrastructure_ShouldReadSecondarySettingsFromSecondarySection_WhenKeysAreConfigured()
    {
        using var provider = TestServices.BuildInfrastructure(new()
        {
            ["Sources:weather:ApiKey"] = "w",
            ["Sources:weather:Secondary:Location"] = "Paris",
        });

        var options = provider.GetRequiredService<IOptions<OpenMeteoOptions>>().Value;

        options.Location.ShouldBe("Paris");
        options.ClientName.ShouldBe("weather-secondary");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddInfrastructure_ShouldDeclareOnlyTheSearchFieldsEachProviderSends_WhenRegistered(bool keyed)
    {
        using var provider = TestServices.BuildInfrastructure(keyed ? new() { ["Sources:weather:ApiKey"] = "w", ["Sources:news:ApiKey"] = "n" } : []);
        const SearchFields weather = SearchFields.City | SearchFields.From;
        const SearchFields news = SearchFields.Text | SearchFields.From | SearchFields.To;

        var sources = Composed(provider);

        sources.Select(s => s.Primary.Source.UsedFields).ShouldBe([weather, news, SearchFields.Text]);
        sources.Select(s => s.Secondary?.Source.UsedFields).ShouldBe(keyed ? [weather, news, null] : [null, null, null]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddInfrastructure_ShouldKeepKeylessProvider_WhenKeyIsBlank(string key)
    {
        using var provider = TestServices.BuildInfrastructure(new() { ["Sources:weather:ApiKey"] = key });

        Composed(provider).First().Primary.Source.ShouldBeOfType<OpenMeteoSource>();
    }

    private static List<ResilientDataSource> Composed(ServiceProvider provider) =>
        [.. provider.GetServices<IDataSource>().Select(source => source.ShouldBeOfType<CachedDataSource>().Inner.ShouldBeOfType<ResilientDataSource>())];

    [Fact]
    public void AddInfrastructure_ShouldNameEverySourceUniquely_WhenRegistered()
    {
        using var provider = TestServices.BuildInfrastructure([]);

        new DataSourceRegistry(provider.GetServices<IDataSource>()).All.Select(s => s.Name)
            .ShouldBe(["weather", "news", "repositories"]);
    }

    [Fact]
    public async Task AddInfrastructure_ShouldReuseRepositoriesPayload_WhenGitHubAnswersNotModified()
    {
        using var http = new MockHttpMessageHandler();
        SourceTestHost.ExpectGitHubSearchThenNotModified(http, "\"v1\"");
        using var provider = TestServices.BuildInfrastructure(
            new() { ["Sources:repositories:Cache:Enabled"] = "false" },
            services => services.AddHttpClient("repositories").ConfigurePrimaryHttpMessageHandler(() => http));
        var source = provider.GetServices<IDataSource>().Single(s => s.Name == "repositories");

        var first = await source.FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);
        var second = await source.FetchAsync(SourceTestHost.Query, TestContext.Current.CancellationToken);

        http.VerifyNoOutstandingExpectation();
        second.ShouldBeOfType<SourceSuccess>().Items.ShouldBe(first.ShouldBeOfType<SourceSuccess>().Items);
    }

    [Theory]
    [InlineData("Sources:news:ApiKey", "bad key")]
    [InlineData("Sources:weather:ApiKey", "line\nbreak")]
    [InlineData("Sources:repositories:Token", "tab\tinside")]
    public async Task AddInfrastructure_ShouldFailAtStartup_WhenSecretIsNotHeaderSafe(string setting, string secret)
    {
        using var provider = TestServices.BuildInfrastructure(new() { [setting] = secret });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() =>
            provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(setting.Split(':')[^1]);
        exception.Message.ShouldNotContain(secret);
    }

    [Theory]
    [InlineData("http://geo.example/", "Sources:weather:GeocodingAddress must use https")]
    [InlineData("https://geo.example/v1", "Sources:weather:GeocodingAddress must end with '/'")]
    public async Task AddInfrastructure_ShouldFailAtStartup_WhenGeocodingAddressIsInvalid(string address, string expected)
    {
        using var provider = TestServices.BuildInfrastructure(new() { ["Sources:weather:GeocodingAddress"] = address });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() =>
            provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(expected);
    }
}
