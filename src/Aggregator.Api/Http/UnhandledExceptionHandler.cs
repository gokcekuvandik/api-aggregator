using Microsoft.AspNetCore.Diagnostics;

namespace Aggregator.Api.Http;

/// <summary>
/// Logs an unhandled exception with its request and answers with a 500 ProblemDetails that reveals nothing.
/// Aborted requests never reach it, and bad requests are left to the middleware, which answers them with their own 4xx status.
/// </summary>
internal sealed partial class UnhandledExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is BadHttpRequestException)
        {
            return false;
        }

        LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path.Value ?? string.Empty);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        // A client that does not accept JSON gets an empty 500; reporting it as unhandled would log the exception twice.
        await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Title = ProblemTitles.UnexpectedError },
        });
        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception while handling {" + LogFields.Method + "} {" + LogFields.Path + "}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);
}
