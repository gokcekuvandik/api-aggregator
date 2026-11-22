namespace Aggregator.Application.Sources;

/// <summary>One provider behind a source, with the most time a single fetch from it may take.</summary>
public sealed record SourceProvider
{
    public SourceProvider(string name, IDataSource source, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        Name = name;
        Source = source;
        Timeout = timeout;
    }

    public string Name { get; }

    public IDataSource Source { get; }

    public TimeSpan Timeout { get; }
}
