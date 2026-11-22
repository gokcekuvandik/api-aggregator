namespace Aggregator.Application.Sources;

/// <summary>Why a source failed, as a caller reads it in <c>sources[].error</c>; never the exception text, which may leak internals.</summary>
public static class SourceFailureMessages
{
    public static string TimedOut(string sourceName) => $"{sourceName} timed out.";

    public static string AnsweredStatus(string sourceName, int status) => $"{sourceName} answered HTTP {status}.";

    public static string Unreachable(string sourceName) => $"{sourceName} could not be reached.";

    public static string Unavailable(string sourceName) => $"{sourceName} is temporarily unavailable.";

    public static string Unreadable(string sourceName) => $"{sourceName} returned an unreadable response.";
}
