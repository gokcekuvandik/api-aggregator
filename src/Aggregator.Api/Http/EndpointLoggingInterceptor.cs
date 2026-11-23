namespace Aggregator.Api.Http;

/// <summary>
/// Logs an API call that answers with an error status, with its endpoint name and, for a validation failure, the names of the invalid fields.
/// Field values are never logged, and a successful call is left to the access log.
/// </summary>
internal sealed partial class EndpointLoggingInterceptor(ILogger<EndpointLoggingInterceptor> logger) : IEndpointFilter
{
    private const int FirstErrorStatus = StatusCodes.Status400BadRequest;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        if (Unwrap(result) is not IStatusCodeHttpResult { StatusCode: >= FirstErrorStatus and var status } outcome)
        {
            return result;
        }

        var endpoint = EndpointName(context.HttpContext);
        if (outcome is IValueHttpResult { Value: HttpValidationProblemDetails validation })
        {
            LogRejected(logger, endpoint, status, string.Join(", ", validation.Errors.Keys.Select(ValidationErrorKeys.ToWireName)));
        }
        else
        {
            LogFailed(logger, endpoint, status);
        }

        return result;
    }

    private static object? Unwrap(object? result)
    {
        while (result is INestedHttpResult nested)
        {
            result = nested.Result;
        }

        return result;
    }

    // A filter only runs on a routed endpoint, and every API endpoint is named.
    private static string EndpointName(HttpContext httpContext) =>
        httpContext.GetEndpoint()!.Metadata.GetRequiredMetadata<IEndpointNameMetadata>().EndpointName;

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Endpoint {" + LogFields.EndpointName + "} rejected the request with {" + LogFields.StatusCode + "}; invalid fields: {" + LogFields.InvalidFields + "}")]
    private static partial void LogRejected(ILogger logger, string endpointName, int statusCode, string invalidFields);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Endpoint {" + LogFields.EndpointName + "} answered {" + LogFields.StatusCode + "}")]
    private static partial void LogFailed(ILogger logger, string endpointName, int statusCode);
}
