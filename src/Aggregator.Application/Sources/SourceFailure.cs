namespace Aggregator.Application.Sources;

/// <summary>The source failed and nothing could stand in for it.</summary>
public sealed record SourceFailure(string Error) : SourceResult([])
{
    public static SourceFailure TimedOut(string sourceName) => new(SourceFailureMessages.TimedOut(sourceName));
}
