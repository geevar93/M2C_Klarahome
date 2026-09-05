using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace KlaraHome.Infrastructure.Correlation;

/// <summary>
/// Accepts an inbound <c>X-Correlation-Id</c> or mints one, publishes it on the scoped
/// <see cref="ICorrelationContext"/> and the current <c>Activity</c>, and echoes it on the
/// response — including on responses written by later middleware or by an exception handler.
/// </summary>
internal sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>An id longer or stranger than this is discarded rather than propagated.</summary>
    private const int MaxAcceptedLength = 128;

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlationContext)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = Accepted(context.Request.Headers[CorrelationHeaders.CorrelationId])
                            ?? CorrelationContext.NewId();

        ((CorrelationContext)correlationContext).Set(correlationId);
        System.Diagnostics.Activity.Current?.SetTag("correlation.id", correlationId);
        context.Items[CorrelationHeaders.CorrelationId] = correlationId;

        context.Response.OnStarting(static state =>
        {
            var ctx = (HttpContext)state;
            if (ctx.Items[CorrelationHeaders.CorrelationId] is string id)
            {
                ctx.Response.Headers[CorrelationHeaders.CorrelationId] = id;
            }

            return Task.CompletedTask;
        }, context);

        await next(context).ConfigureAwait(false);
    }

    private static string? Accepted(StringValues header)
    {
        var value = header.Count > 0 ? header[0] : null;

        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxAcceptedLength)
        {
            return null;
        }

        // Reject anything that could forge a log line or a response header.
        foreach (var character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.' or ':'))
            {
                return null;
            }
        }

        return value;
    }
}
