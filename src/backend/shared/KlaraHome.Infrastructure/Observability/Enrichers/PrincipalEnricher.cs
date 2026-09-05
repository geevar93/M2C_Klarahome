using KlaraHome.Infrastructure.Authorization;
using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace KlaraHome.Infrastructure.Observability.Enrichers;

/// <summary>
/// Stamps the authenticated subject and vendor onto every log event. Ids only — never a name,
/// mobile number or email (docs/07-security-compliance.md §3, logging hygiene).
/// </summary>
/// <remarks>
/// The claim types are the ones the Identity module issues; they are named once in
/// <see cref="KlaraHomeClaims"/> so the issuer and every reader cannot drift apart.
/// </remarks>
internal sealed class PrincipalEnricher(IHttpContextAccessor accessor) : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        Add(logEvent, propertyFactory, "userId", principal.FindFirst(KlaraHomeClaims.UserId)?.Value);
        Add(logEvent, propertyFactory, "vendorId", principal.FindFirst(KlaraHomeClaims.VendorId)?.Value);
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
