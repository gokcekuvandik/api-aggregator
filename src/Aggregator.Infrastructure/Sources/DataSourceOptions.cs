using System.ComponentModel.DataAnnotations;

namespace Aggregator.Infrastructure.Sources;

/// <summary>Settings every HTTP source shares, bound from <c>Sources:{name}</c>; subclasses set a default address.</summary>
public abstract class DataSourceOptions
{
    public const string SectionName = "Sources";

    /// <summary>Visible ASCII only, so a key or token can go into a header or query string without breaking the request.</summary>
    internal const string HeaderSafePattern = @"^[!-~]+$";

    internal const string HeaderSafeMessage = "{0} may contain only visible ASCII characters, with no spaces.";

    private const string SecondarySectionName = "Secondary";

    internal static string SectionFor(string sourceName) => $"{SectionName}:{sourceName}";

    internal static string SecondarySectionFor(string sourceName) => $"{SectionFor(sourceName)}:{SecondarySectionName}";

    public Uri? BaseAddress { get; set; }

    /// <summary>The most time one fetch from this provider may take, every HTTP call and retry included.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:01:00")]
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The named HTTP client the provider sends on; set at registration, never bound from configuration.</summary>
    internal string? ClientName { get; set; }

    internal SourceClient ClientFor(string sourceName) => new(sourceName, ClientName ?? sourceName);

    /// <summary>Every upstream address the source calls, by setting name; each must pass the same checks as the base address.</summary>
    internal virtual IEnumerable<(string Key, Uri? Address)> Addresses => [(nameof(BaseAddress), BaseAddress)];
}
