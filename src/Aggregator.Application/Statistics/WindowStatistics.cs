namespace Aggregator.Application.Statistics;

/// <summary>Completed requests within the last <see cref="Length"/>.</summary>
public sealed record WindowStatistics(TimeSpan Length, long Count, TimeSpan AverageDuration);
