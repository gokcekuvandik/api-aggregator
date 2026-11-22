using Aggregator.Application.Statistics;
using Aggregator.Domain.Statistics;
using Aggregator.Infrastructure.Sources.Http;
using Aggregator.Infrastructure.Statistics;
using Aggregator.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aggregator.Infrastructure.UnitTests.Statistics;

public sealed class StatisticsRegistrationTests
{
    private static readonly Dictionary<string, string?> Keyed = new() { ["Sources:news:ApiKey"] = "n" };

    [Fact]
    public void AddInfrastructure_ShouldReadWhatTheTelemetryRecorded_WhenBothComeFromTheContainer()
    {
        using var provider = TestServices.BuildInfrastructure([]);

        provider.GetRequiredService<ISourceTelemetry>().RecordRequest(new("news", null), TimeSpan.FromMilliseconds(250), RequestOutcome.Success, 1);

        var snapshot = provider.GetRequiredService<IStatisticsReader>().GetSnapshots().ShouldHaveSingleItem();
        snapshot.Performance.ShouldBe(PerformanceBucket.Slow);
    }

    [Fact]
    public void AddInfrastructure_ShouldClassifyWithConfiguredBuckets_WhenStatisticsBucketsAreSet()
    {
        using var provider = TestServices.BuildInfrastructure(new()
        {
            ["Statistics:Buckets:FastBelow"] = "00:00:00.300",
            ["Statistics:Buckets:SlowAbove"] = "00:00:01",
        });

        provider.GetRequiredService<ISourceTelemetry>().RecordRequest(new("news", null), TimeSpan.FromMilliseconds(250), RequestOutcome.Success, 1);

        provider.GetRequiredService<IStatisticsReader>().GetSnapshots().ShouldHaveSingleItem().Performance.ShouldBe(PerformanceBucket.Fast);
    }

    [Fact]
    public void AddStatistics_ShouldRegisterOneStore_WhenCalledTwice()
    {
        var services = TestServices.Create([]);

        services.AddStatistics().AddStatistics();

        services.Count(d => d.ServiceType == typeof(StatisticsStore)).ShouldBe(1);
        services.Count(d => d.ServiceType == typeof(ISourceTelemetry)).ShouldBe(1);
    }

    [Theory]
    [InlineData("00:00:00", "00:00:00.200")]
    [InlineData("00:00:00.300", "00:00:00.200")]
    public void AddInfrastructure_ShouldRejectTheSettings_WhenBucketsAreNotPositiveAndOrdered(string fastBelow, string slowAbove)
    {
        using var provider = TestServices.BuildInfrastructure(new()
        {
            ["Statistics:Buckets:FastBelow"] = fastBelow,
            ["Statistics:Buckets:SlowAbove"] = slowAbove,
        });

        var error = Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<StatisticsOptions>>().Value);

        error.Message.ShouldContain("Statistics:Buckets");
    }

    [Theory]
    [InlineData("news", "news", null)]
    [InlineData("news-secondary", "news", "hacker-news")]
    [InlineData("repositories", "repositories", null)]
    public void AddInfrastructure_ShouldMeasureOutsideTheResiliencePipelineAndCountAttemptsInsideIt_WhenAClientIsBuilt(
        string clientName, string source, string? provider)
    {
        using var services = TestServices.BuildInfrastructure(Keyed);

        var chain = Chain(services.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName));

        var statistics = chain.OfType<StatisticsHandler>().ShouldHaveSingleItem();
        statistics.Api.ShouldBe(new UpstreamApi(source, provider));
        var names = chain.Select(handler => handler.GetType().Name).ToList();
        var resilience = names.Select((name, index) => (name, index)).Where(h => h.name == "ResilienceHandler").Select(h => h.index).ToList();
        resilience.Count.ShouldBe(2, "the standard pipeline and the chaos pipeline");
        names.IndexOf(nameof(StatisticsHandler)).ShouldBeLessThan(resilience[0]);
        names.IndexOf(nameof(AttemptCountingHandler)).ShouldBeInRange(resilience[0] + 1, resilience[1] - 1);
        names.IndexOf(nameof(RateLimitLoggingHandler)).ShouldBeGreaterThan(resilience[1]);
    }

    private static List<HttpMessageHandler> Chain(HttpMessageHandler handler)
    {
        List<HttpMessageHandler> chain = [];
        for (HttpMessageHandler? current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
        {
            chain.Add(current);
        }

        return chain;
    }
}
