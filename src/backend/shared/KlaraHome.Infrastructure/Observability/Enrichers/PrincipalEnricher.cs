using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace KlaraHome.Infrastructure.Observability.Enrichers;

/// <summary>
/// Stamps the authenticated subject and vendor onto every log event. Ids only — never a name,
/// mobile number or email (docs/07-security-compliance.md §3, logging hygiene).
/// </summary>
/// <remarks>
/// The claim types are the ones the Identity module issues from Step 7. Until then no principal
/// is authenticated and this enricher is a no-op.
/// </remarks>
internal sealed class PrincipalEnricher(IHttpContextAccessor accessor) : ILogEventEnricher
{
    internal const string UserIdClaim = "sub";
    internal const string VendorIdClaim = "vendor_id";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        Add(logEvent, propertyFactory, "userId", principal.FindFirst(UserIdClaim)?.Value);
        Add(logEvent, propertyFactory, "vendorId", principal.FindFirst(VendorIdClaim)?.Value);
    }

    private static void Add(
        LogEvent logEvent,
        ILogEventPropertyFactory propertyFactory,
        string name,
        string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(name, value));
        }
    }
}
