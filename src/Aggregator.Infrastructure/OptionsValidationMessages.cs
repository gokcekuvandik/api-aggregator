using Aggregator.Infrastructure.Sources;
using Aggregator.Infrastructure.Sources.Resilience;

namespace Aggregator.Infrastructure;

/// <summary>Startup failures for source, cache and statistics settings, each naming the configuration key to fix.</summary>
internal static class OptionsValidationMessages
{
    /// <summary>A data annotation format; <c>{0}</c> is the property name.</summary>
    public const string HeaderSafe = "{0} may contain only visible ASCII characters, with no spaces.";

    public static string InSection(string section, string? message) => $"{section}: {message}";

    public static string Required(string key) => $"{key} is required.";

    public static string NotAbsolute(string key) => $"{key} must be an absolute URI.";

    public static string NotSecure(string key) => $"{key} must use https; plain http is allowed only for loopback hosts.";

    public static string HasCredentials(string key) => $"{key} must not contain credentials; use the source's key or token setting.";

    public static string NoTrailingSlash(string key) => $"{key} must end with '/' so relative request paths keep its path.";

    public static string OutOfRange(string section, string setting, TimeSpan min, TimeSpan max) => $"{section}:{setting} must be from {min} to {max}.";

    public static string InvalidRedisConnection(string name) => $"ConnectionStrings:{name} must be a valid Redis connection string when it is set.";

    public static string ChaosInProduction(string section) =>
        $"{section}:{nameof(ChaosOptions.Enabled)} must be false in Production; chaos is for demos and tests.";

    public static string NotARate(string section, string setting) => $"{section}:{setting} must be between 0 and 1.";

    public static string LatencyOutOfRange(string section, TimeSpan max) => $"{section}:{nameof(ChaosOptions.Latency)} must be between 00:00:00 and {max}.";

    public static string TimeoutWithinPipeline(string section, TimeSpan timeout, string resilienceSection, TimeSpan total) =>
        $"{section}:{nameof(DataSourceOptions.Timeout)} ({timeout}) must be longer than " +
        $"{resilienceSection}:TotalRequestTimeout:Timeout ({total}).";

    public static string InvalidBuckets(string section) => InSection(section, "FastBelow must be positive and SlowAbove must not be shorter than it.");

    public static string InvalidAnomalies(string section) =>
        InSection(section, "Interval must be positive and at most a day, Threshold positive, and MinSamples at least 1.");
}
