using KlaraHome.Infrastructure.Correlation;
using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace KlaraHome.Infrastructure.Observability.Enrichers;

/// <summary>Stamps every log event with the correlation id of the request that produced it.</summary>
internal sealed class CorrelationIdEnricher(IHttpContextAccessor accessor) : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        if (accessor.HttpContext?.Items[CorrelationHeaders.CorrelationId] is string correlationId)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("correlationId", correlationId));
        }
    }
}
