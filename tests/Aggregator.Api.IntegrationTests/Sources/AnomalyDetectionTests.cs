using Aggregator.Api.IntegrationTests.Infrastructure;
using Aggregator.Application.Statistics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Aggregator.Api.IntegrationTests.Sources;

public sealed class AnomalyDetectionTests(AggregatorApiFactory factory) : IClassFixture<AggregatorApiFactory>
{
    private const string DetectorCategory = "Aggregator.Infrastructure.Statistics.AnomalyDetector";
    private static readonly UpstreamApi News = new("news", null);

    [Fact]
    public async Task Host_ShouldLogOneAnomalyWarning_WhenRecentRequestsAreMuchSlowerThanTheOverallAverage()
    {
        var time = new FakeTimeProvider();
        var logs = new CapturingLoggerProvider();
        // The only check runs at 6 minutes, after both recordings, when the fast history has left the 5-minute window.
        var (client, services) = factory.CreateWithUpstreams(
            new Dictionary<string, string?> { ["Statistics:Anomalies:Interval"] = "00:06:00" },
            configure: s => s.Replace(ServiceDescriptor.Singleton<TimeProvider>(time)),
            logs: logs);
        using var _ = client;
        var telemetry = services.GetRequiredService<ISourceTelemetry>();

        Record(telemetry, count: 50, milliseconds: 20);
        time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(10));
        Record(telemetry, count: 12, milliseconds: 300);
        time.Advance(TimeSpan.FromSeconds(50));

        var warning = await WaitForAsync(() => logs.InCategory(DetectorCategory).Where(e => e.Level == LogLevel.Warning).ToList());
        warning.ShouldHaveSingleItem().State.ShouldSatisfyAllConditions(
            state => state["Source"].ShouldBe("news"),
            state => state["SampleCount"].ShouldBe("12"));
    }

    private static void Record(ISourceTelemetry telemetry, int count, double milliseconds)
    {
        for (var i = 0; i < count; i++)
        {
            telemetry.RecordRequest(News, TimeSpan.FromMilliseconds(milliseconds), RequestOutcome.Success, attempts: 1);
        }
    }

    private static async Task<List<CapturedLog>> WaitForAsync(Func<List<CapturedLog>> read)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (read() is { Count: 0 })
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        return read();
    }
}
