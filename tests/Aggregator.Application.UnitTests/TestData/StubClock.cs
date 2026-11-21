namespace Aggregator.Application.UnitTests.TestData;

/// <summary>A clock that reads a set instant until the test advances it.</summary>
internal sealed class StubClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public void Advance(TimeSpan by) => _now += by;

    public override DateTimeOffset GetUtcNow() => _now;
}
