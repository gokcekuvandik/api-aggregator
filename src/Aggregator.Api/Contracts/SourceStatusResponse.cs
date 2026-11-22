using System.Diagnostics;
using Aggregator.Application.Aggregation;
using Aggregator.Application.Sources;

namespace Aggregator.Api.Contracts;

/// <summary>
/// How one source fared. A 200 can still contain <see cref="SourceStatus.Failed"/> entries.
/// A <see cref="SourceStatus.Fallback"/> names the secondary <c>provider</c> that answered live (age 0),
/// or has no provider when the items are last-known-good data of the given age.
/// A <see cref="SourceStatus.Cached"/> entry gives the cache entry's age and, when the secondary had answered, its provider.
/// </summary>
public sealed record SourceStatusResponse(
    string Name,
    SourceStatus Status,
    double DurationMs,
    string? Error,
    double? AgeSeconds,
    string? Provider)
{
    public static SourceStatusResponse From(SourceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.Result switch
        {
            SourceSuccess => Status(SourceStatus.Success),
            SourceCached cached => Status(SourceStatus.Cached, ageSeconds: cached.Age.TotalSeconds, provider: cached.Provider),
            SourceFallback fallback => Status(SourceStatus.Fallback, ageSeconds: fallback.Age.TotalSeconds, provider: fallback.Provider),
            SourceFailure failure => Status(SourceStatus.Failed, error: failure.Error),
            _ => throw new UnreachableException($"Unknown source result {report.Result.GetType().Name}."),
        };

        SourceStatusResponse Status(SourceStatus status, string? error = null, double? ageSeconds = null, string? provider = null) =>
            new(report.Name, status, report.Duration.TotalMilliseconds, error, ageSeconds, provider);
    }
}
