namespace Aggregator.Infrastructure.Sources;

/// <summary>Settings every HTTP source shares, bound from <c>Sources:{name}</c>; subclasses set a default address.</summary>
public abstract class DataSourceOptions
{
    public const string SectionName = "Sources";

    /// <summary>Visible ASCII only, so a key or token can go into a header or query string without breaking the request.</summary>
    internal const string HeaderSafePattern = @"^[!-~]+$";

    internal const string HeaderSafeMessage = "{0} may contain only visible ASCII characters, with no spaces.";

    internal static string SectionFor(string sourceName) => $"{SectionName}:{sourceName}";

    public Uri? BaseAddress { get; set; }
}
