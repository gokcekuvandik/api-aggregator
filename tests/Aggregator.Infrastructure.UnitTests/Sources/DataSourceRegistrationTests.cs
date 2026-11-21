using System.ComponentModel.DataAnnotations;
using System.Net;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;
using Aggregator.Infrastructure.Sources;
using Aggregator.Infrastructure.Sources.Http;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace Aggregator.Infrastructure.UnitTests.Sources;

public sealed class DataSourceRegistrationTests
{
    private const string Name = "fake";

    [Fact]
    public void AddDataSource_ShouldBindOptions_WhenSourcesSectionHasValues()
    {
        using var provider = Build(new() { ["Sources:fake:BaseAddress"] = "https://fake.test/", ["Sources:fake:MaxItems"] = "7" });

        var options = provider.GetRequiredService<IOptions<FakeOptions>>().Value;

        options.BaseAddress.ShouldBe(new Uri("https://fake.test/"));
        options.MaxItems.ShouldBe(7);
    }

    [Fact]
    public void AddDataSource_ShouldKeepDefaults_WhenSourcesSectionIsMissing()
    {
        using var provider = Build([]);

        var options = provider.GetRequiredService<IOptions<FakeOptions>>().Value;

        options.BaseAddress.ShouldBe(FakeOptions.DefaultBaseAddress);
        options.MaxItems.ShouldBe(10);
    }

    [Fact]
    public void AddDataSource_ShouldConfigureNamedClient_WhenClientIsCreated()
    {
        using var provider = Build(new() { ["Sources:fake:BaseAddress"] = "https://fake.test/api/" });

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(Name);

        client.BaseAddress.ShouldBe(new Uri("https://fake.test/api/"));
        client.DefaultRequestHeaders.UserAgent.ToString().ShouldStartWith("AgileActorsAggregator/");
    }

    [Fact]
    public void AddDataSource_ShouldExposeSourceAsDataSource_WhenResolved()
    {
        using var provider = Build([]);

        var source = Composed(provider);

        source.Name.ShouldBe(Name);
        source.Primary.Source.ShouldBeOfType<FakeSource>();
        source.Primary.Timeout.ShouldBe(TimeSpan.FromSeconds(10));
        source.Secondary.ShouldBeNull();
    }

    [Fact]
    public void AddDataSource_ShouldUseConfiguredTimeout_WhenSet()
    {
        using var provider = Build(new()
        {
            ["Sources:fake:Timeout"] = "00:00:04",
            ["Sources:fake:Resilience:TotalRequestTimeout:Timeout"] = "00:00:03",
        });

        Composed(provider).Primary.Timeout.ShouldBe(TimeSpan.FromSeconds(4));
    }

    [Theory]
    [InlineData("00:00:00.5")]
    [InlineData("00:01:01")]
    public async Task AddDataSource_ShouldFailAtStartup_WhenTimeoutIsOutOfRange(string timeout)
    {
        using var provider = Build(new() { ["Sources:fake:Timeout"] = timeout });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => ValidateAtStartupAsync(provider));

        exception.Message.ShouldContain(nameof(DataSourceOptions.Timeout));
    }

    [Fact]
    public void AddSecondaryDataSource_ShouldComposeBothProviders_WhenRegistered()
    {
        using var provider = BuildWithSecondary([]);

        var source = Composed(provider);

        source.Primary.Source.ShouldBeOfType<FakeSource>();
        var secondary = source.Secondary.ShouldNotBeNull();
        secondary.Name.ShouldBe("fake-backup");
        secondary.Source.ShouldBeOfType<BackupSource>().Name.ShouldBe(Name);
    }

    [Fact]
    public void AddSecondaryDataSource_ShouldBindOwnSectionAndClient_WhenRegistered()
    {
        using var provider = BuildWithSecondary(new()
        {
            ["Sources:fake:BaseAddress"] = "https://primary.test/",
            ["Sources:fake:Secondary:BaseAddress"] = "https://secondary.test/",
            ["Sources:fake:Secondary:Timeout"] = "00:00:03",
            ["Sources:fake:Secondary:Resilience:TotalRequestTimeout:Timeout"] = "00:00:02",
            ["Sources:fake:Secondary:Resilience:AttemptTimeout:Timeout"] = "00:00:01",
        });

        var options = provider.GetRequiredService<IOptions<BackupOptions>>().Value;
        using var primaryClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(Name);
        using var secondaryClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient("fake-secondary");

        options.ClientName.ShouldBe("fake-secondary");
        primaryClient.BaseAddress.ShouldBe(new Uri("https://primary.test/"));
        secondaryClient.BaseAddress.ShouldBe(new Uri("https://secondary.test/"));
        Composed(provider).Secondary!.Timeout.ShouldBe(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task AddSecondaryDataSource_ShouldNameItsOwnSection_WhenItsOptionsAreInvalid()
    {
        using var provider = BuildWithSecondary(new() { ["Sources:fake:Secondary:BaseAddress"] = "http://secondary.test/" });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => ValidateAtStartupAsync(provider));

        exception.Message.ShouldContain("Sources:fake:Secondary:BaseAddress");
    }

    [Fact]
    public void AddSecondaryDataSource_ShouldThrowOnResolve_WhenSecondaryReportsAnotherName()
    {
        var services = Services([]);
        services.AddDataSource<FakeSource, FakeOptions>("other");
        services.AddSecondaryDataSource<BackupSource, BackupOptions>("other", "fake-backup");
        using var provider = services.BuildServiceProvider();

        Should.Throw<InvalidOperationException>(() => provider.GetServices<IDataSource>().ToList()).Message.ShouldContain("other");
    }

    [Fact]
    public void AddDataSource_ShouldCapResponseSize_WhenClientIsCreated()
    {
        using var provider = Build([]);

        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(Name);

        client.MaxResponseContentBufferSize.ShouldBe(4 * 1024 * 1024);
    }

    [Theory]
    [InlineData("https://fake.test/")]
    [InlineData("http://localhost:5000/")]
    [InlineData("http://127.0.0.1:5000/api/")]
    public async Task AddDataSource_ShouldAcceptBaseAddress_WhenItIsHttpsOrLoopback(string baseAddress)
    {
        using var provider = Build(new() { ["Sources:fake:BaseAddress"] = baseAddress });

        await Should.NotThrowAsync(() => ValidateAtStartupAsync(provider));
    }

    [Theory]
    [InlineData("relative/path", "absolute")]
    [InlineData("ftp://fake.test/", "https")]
    [InlineData("http://fake.test/", "https")]
    [InlineData("https://fake.test/api", "end with '/'")]
    [InlineData("https://user:pass@fake.test/", "credentials")]
    [InlineData("", "required")]
    public async Task AddDataSource_ShouldFailAtStartup_WhenBaseAddressIsInvalid(string baseAddress, string expected)
    {
        using var provider = Build(new() { ["Sources:fake:BaseAddress"] = baseAddress });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => ValidateAtStartupAsync(provider));

        exception.Message.ShouldContain("Sources:fake:BaseAddress");
        exception.Message.ShouldContain(expected, Case.Insensitive);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    public async Task AddDataSource_ShouldFailAtStartup_WhenAnnotatedOptionIsOutOfRange(string maxItems)
    {
        using var provider = Build(new() { ["Sources:fake:MaxItems"] = maxItems });

        var exception = await Should.ThrowAsync<OptionsValidationException>(() => ValidateAtStartupAsync(provider));

        exception.Message.ShouldContain(nameof(FakeOptions.MaxItems));
    }

    [Fact]
    public async Task AddDataSource_ShouldPassStartupValidation_WhenOptionsAreValid()
    {
        using var provider = Build([]);

        await Should.NotThrowAsync(() => ValidateAtStartupAsync(provider));
    }

    [Fact]
    public void AddSecondaryDataSource_ShouldThrow_WhenProviderTypeIsAlreadyRegistered()
    {
        var services = Services([]);
        services.AddDataSource<FakeSource, FakeOptions>(Name);

        var exception = Should.Throw<InvalidOperationException>(() => services.AddSecondaryDataSource<FakeSource, FakeOptions>(Name, "again"));

        exception.Message.ShouldContain(nameof(FakeSource));
    }

    [Fact]
    public void AddDataSource_ShouldThrowOnResolve_WhenSourceNameDiffersFromRegisteredName()
    {
        var services = Services([]);
        services.AddDataSource<FakeSource, FakeOptions>("other");
        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<InvalidOperationException>(() => provider.GetServices<IDataSource>().ToList());

        exception.Message.ShouldContain("other");
        exception.Message.ShouldContain(Name);
    }

    [Fact]
    public async Task AddDataSource_ShouldLogRateLimitHeaders_WhenSourceClientReceivesThem()
    {
        using var http = new MockHttpMessageHandler();
        http.When("*").Respond(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "1");
            return response;
        });
        var services = Services([]);
        services.AddFakeLogging();
        services.AddDataSource<FakeSource, FakeOptions>(Name).ConfigurePrimaryHttpMessageHandler(() => http);
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(Name);

        using var response = await client.GetAsync(new Uri("items", UriKind.Relative), TestContext.Current.CancellationToken);

        provider.GetFakeLogCollector().GetSnapshot().ShouldContain(record =>
            record.Category == typeof(RateLimitLoggingHandler).FullName && record.Level == LogLevel.Warning);
    }

    private static ResilientDataSource Composed(ServiceProvider provider) =>
        provider.GetServices<IDataSource>().ShouldHaveSingleItem().ShouldBeOfType<CachedDataSource>().Inner.ShouldBeOfType<ResilientDataSource>();

    private static async Task ValidateAtStartupAsync(ServiceProvider provider) =>
        await provider.GetRequiredService<IAsyncStartupValidator>().ValidateAsync(TestContext.Current.CancellationToken);

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var services = Services(settings);
        services.AddDataSource<FakeSource, FakeOptions>(Name);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildWithSecondary(Dictionary<string, string?> settings)
    {
        var services = Services(settings);
        services.AddDataSource<FakeSource, FakeOptions>(Name);
        services.AddSecondaryDataSource<BackupSource, BackupOptions>(Name, "fake-backup");
        return services.BuildServiceProvider();
    }

    private static ServiceCollection Services(Dictionary<string, string?> settings) => TestServices.Create(settings);

    public sealed class BackupOptions : DataSourceOptions
    {
        public BackupOptions() => BaseAddress = new Uri("https://backup.test/");
    }

    public sealed class BackupSource : IDataSource
    {
        public string Name => DataSourceRegistrationTests.Name;

        public IReadOnlyList<string> Categories { get; } = ["fake"];

        public Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<SourceResult>(new SourceSuccess([]));
    }

    public sealed class FakeOptions : DataSourceOptions
    {
        public static readonly Uri DefaultBaseAddress = new("https://default.test/");

        public FakeOptions() => BaseAddress = DefaultBaseAddress;

        [Range(1, 100)]
        public int MaxItems { get; set; } = 10;
    }

    public sealed class FakeSource : IDataSource
    {
        public string Name => DataSourceRegistrationTests.Name;

        public IReadOnlyList<string> Categories { get; } = ["fake"];

        public Task<SourceResult> FetchAsync(AggregationQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<SourceResult>(new SourceSuccess([]));
    }
}
