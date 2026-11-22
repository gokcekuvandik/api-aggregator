using Aggregator.Application.Telemetry;

namespace Aggregator.ArchitectureTests;

/// <summary>The service defaults cannot reference the application, so the shared telemetry name is pinned here instead.</summary>
public sealed class TelemetryConventionTests
{
    [Fact]
    public void ServiceDefaults_ShouldListenToTheApplicationsMeterAndSource_WhenNamesAreCompared() =>
        Microsoft.Extensions.Hosting.Extensions.AggregatorTelemetryName.ShouldBe(AggregatorDiagnostics.Name);
}
