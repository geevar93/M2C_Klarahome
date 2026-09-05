namespace KlaraHome.Infrastructure.Correlation;

/// <summary>Header names used for request correlation (docs/04-api-specification.md §1).</summary>
public static class CorrelationHeaders
{
    /// <summary>Accepted from the caller if present, generated otherwise, echoed on every response.</summary>
    public const string CorrelationId = "X-Correlation-Id";

    /// <summary>The W3C trace context header, propagated by OpenTelemetry.</summary>
    public const string TraceParent = "traceparent";
}
