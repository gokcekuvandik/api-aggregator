namespace Aggregator.Infrastructure.UnitTests.TestDoubles;

/// <summary>A clock that reads a set instant until the test advances it; timestamps follow the same instant.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    private long _ticks = now.UtcTicks;

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);
}
