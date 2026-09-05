using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace KlaraHome.Infrastructure.Observability.Enrichers;

/// <summary>
/// Copies the ambient W3C trace context onto the log event, so a log line in Loki links straight
/// to its trace in Tempo (docs/09-nfr-testing-observability.md §3.3).
/// </summary>
internal sealed class ActivityEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("traceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("spanId", activity.SpanId.ToString()));
    }
}
