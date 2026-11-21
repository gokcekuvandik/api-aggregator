namespace Aggregator.Infrastructure.Sources;

/// <summary>Public source names; they stay the same whichever provider serves the category.</summary>
internal static class SourceNames
{
    public const string Weather = "weather";
    public const string News = "news";
    public const string Repositories = "repositories";
}

/// <summary>Provider names reported to clients when a secondary provider served a source.</summary>
internal static class ProviderNames
{
    public const string OpenMeteo = "open-meteo";
    public const string HackerNews = "hacker-news";
}
