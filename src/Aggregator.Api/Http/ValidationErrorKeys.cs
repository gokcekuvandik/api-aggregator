using System.Text.Json;

namespace Aggregator.Api.Http;

/// <summary>
/// Built-in validation keys errors by CLR member name ("PageSize"). Clients send and read
/// camelCase ("pageSize"), so the keys are rewritten once here for every endpoint (ADR-0002).
/// </summary>
internal static class ValidationErrorKeys
{
    public static void CamelCase(ProblemDetailsContext context)
    {
        if (context.ProblemDetails is not HttpValidationProblemDetails validation)
        {
            return;
        }

        validation.Errors = validation.Errors
            .GroupBy(error => ToCamelCasePath(error.Key), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.SelectMany(error => error.Value).ToArray(), StringComparer.Ordinal);
    }

    /// <summary>Camel-cases each segment of a path such as <c>Items[0].PageSize</c>.</summary>
    internal static string ToCamelCasePath(string path) =>
        string.Join('.', path.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
}
