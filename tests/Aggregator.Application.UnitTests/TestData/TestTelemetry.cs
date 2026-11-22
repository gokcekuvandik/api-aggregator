using Aggregator.Application.Telemetry;

namespace Aggregator.Application.UnitTests.TestData;

/// <summary>Telemetry for tests that do not assert on it; its meters live as long as the test run.</summary>
internal static class TestTelemetry
{
    public static AggregationTelemetry Unobserved { get; } = new(new TestMeterFactory());
}
