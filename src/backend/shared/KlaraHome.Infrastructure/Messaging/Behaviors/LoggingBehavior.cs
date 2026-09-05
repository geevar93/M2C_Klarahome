using System.Diagnostics;
using KlaraHome.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace KlaraHome.Infrastructure.Messaging.Behaviors;

/// <summary>
/// Logs one line per handled request with its outcome and duration. The request itself is never
/// logged — payloads carry PII and are the caller's business (docs/07-security-compliance.md §3).
/// </summary>
internal sealed partial class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> HandleAsync(
        TRequest request,
        PipelineNext<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var timestamp = Stopwatch.GetTimestamp();

        try
        {
            var response = await next().ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;

            if (response.IsSuccess)
            {
                Handled(logger, requestName, elapsed);
            }
            else
            {
                Refused(logger, requestName, response.Error.Code, response.Error.Type.ToString(), elapsed);
            }

            return response;
        }
        catch (Exception exception)
        {
            Faulted(logger, requestName, Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds, exception);
            throw;
        }
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "Handled {RequestName} in {ElapsedMs:0.##} ms")]
    private static partial void Handled(ILogger logger, string requestName, double elapsedMs);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning,
        Message = "Refused {RequestName} with {ErrorCode} ({ErrorType}) in {ElapsedMs:0.##} ms")]
    private static partial void Refused(
        ILogger logger, string requestName, string errorCode, string errorType, double elapsedMs);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error,
        Message = "Faulted {RequestName} after {ElapsedMs:0.##} ms")]
    private static partial void Faulted(ILogger logger, string requestName, double elapsedMs, Exception exception);
}
