using System.Text.Json;

namespace Aggregator.Api.Http;

/// <summary>
/// Built-in validation keys errors by CLR member name ("PageSize"), and .NET 11 offers no hook to
/// change that. Clients send and read camelCase, so keys are rewritten once here for every endpoint.
/// </summary>
/// <remarks>
/// A heuristic: it assumes the wire name is camelCase(CLR name), which holds for every bound
/// parameter today. A parameter whose wire name differs (for example <c>[FromQuery(Name = "q")]</c>)
/// would need a custom <c>IValidatableInfoResolver</c> instead. Keys that collapse to the same
/// name are merged rather than throwing on the error path.
/// </remarks>
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
