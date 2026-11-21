using System.Text.Json;

namespace Aggregator.Api.Http;

/// <summary>
/// Rewrites validation error keys to camelCase so they match the query parameter names.
/// It assumes each wire name is the camelCase of its C# name.
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
            .GroupBy(error => JsonNamingPolicy.CamelCase.ConvertName(error.Key), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.SelectMany(error => error.Value).ToArray(), StringComparer.Ordinal);
    }
}
