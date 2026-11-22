namespace Aggregator.Api.Http;

/// <summary>Titles and details of the ProblemDetails the API answers with.</summary>
internal static class ProblemTitles
{
    public const string UnexpectedError = "An unexpected error occurred";
    public const string NoSourceData = "No source returned data";
    public const string NoSourceDataDetail = "Every source failed and no fallback data was available. See sources for details.";
}
