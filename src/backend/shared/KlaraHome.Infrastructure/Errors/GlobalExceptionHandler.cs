using KlaraHome.SharedKernel.Results;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Infrastructure.Errors;

/// <summary>
/// The last line of defence. Anything that escapes a handler becomes a 500 ProblemDetails
/// carrying only a correlation id — the stack trace goes to the log, never to the client
/// (docs/04-api-specification.md §1.2, docs/07-security-compliance.md §3).
/// </summary>
internal sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // The client hung up. Nothing to report and nothing to write to.
            return true;
        }

        var error = MapToError(exception);
        var problem = error.ToProblemDetails(httpContext);
        var correlationId = problem.Extensions.TryGetValue(
            ProblemDetailsExtensions.CorrelationIdExtension, out var value) ? value as string : null;

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            Unhandled(logger, httpContext.Request.Method, httpContext.Request.Path, correlationId, exception);
        }
        else
        {
            Rejected(logger, httpContext.Request.Method, httpContext.Request.Path, error.Code, correlationId);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        await httpContext.Response
            .WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Only exceptions the framework raises for a malformed request are translated. Everything
    /// else is genuinely unexpected and stays a 500 — business refusals travel as
    /// <see cref="Result"/>, not as exceptions.
    /// </summary>
    private static Error MapToError(Exception exception) => exception switch
    {
        BadHttpRequestException => Error.Malformed("MALFORMED_REQUEST", "The request could not be read."),
        System.Text.Json.JsonException => Error.Malformed("MALFORMED_JSON", "The request body is not valid JSON."),
        TimeoutException => Error.Unavailable("DEPENDENCY_TIMEOUT", "A dependency did not respond in time."),
        _ => Error.Unexpected(),
    };

    [LoggerMessage(EventId = 1100, Level = LogLevel.Error,
        Message = "Unhandled exception for {Method} {Path} (correlationId {CorrelationId})")]
    private static partial void Unhandled(
        ILogger logger, string method, string path, string? correlationId, Exception exception);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning,
        Message = "Rejected {Method} {Path} with {ErrorCode} (correlationId {CorrelationId})")]
    private static partial void Rejected(
        ILogger logger, string method, string path, string errorCode, string? correlationId);
}
