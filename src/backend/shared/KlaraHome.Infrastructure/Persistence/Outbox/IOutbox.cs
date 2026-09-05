using System.Text.Json;
using KlaraHome.Contracts.IntegrationEvents;
using KlaraHome.Infrastructure.Correlation;
using KlaraHome.SharedKernel.Time;

namespace KlaraHome.Infrastructure.Persistence.Outbox;

/// <summary>
/// Publishes an integration event by appending it to the caller's own <c>DbContext</c>. Nothing
/// is sent here and nothing is committed here: the event becomes real when the caller's
/// <c>SaveChangesAsync</c> commits, and not before.
/// </summary>
/// <remarks>
/// Resolved per module context, so the write lands in the transaction the handler is already in.
/// A handler that publishes and then throws publishes nothing — which is the guarantee the
/// pattern exists to provide (ADR-003).
/// </remarks>
public interface IOutbox
{
    /// <summary>Queues an event for dispatch after the current transaction commits.</summary>
    /// <param name="integrationEvent">The fact to announce.</param>
    void Enqueue(IIntegrationEvent integrationEvent);
}

/// <summary>
/// The serialisation contract for outbox payloads, shared by the writer and the dispatcher so
/// they cannot drift apart.
/// </summary>
public static class OutboxSerialization
{
    /// <summary>
    /// Property names stay as declared. Integration events are a wire contract read by code, not
    /// by a browser, and a casing convention applied on one side and not the other is a class of
    /// bug worth designing out.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = null,
        WriteIndented = false,
    };

    /// <summary>
    /// The stored type name: the CLR full name without assembly or version, so moving a contract
    /// between assemblies does not strand a queued message.
    /// </summary>
    /// <param name="type">The event type.</param>
    public static string NameOf(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.FullName ?? type.Name;
    }
}

/// <param name="context">The module context whose transaction the message joins.</param>
/// <param name="tenantContext">Stamps the owning tenant.</param>
/// <param name="correlationContext">Carries the causing request's id across the async boundary.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class DbContextOutbox(
    KlaraHomeDbContext context,
    Tenancy.ITenantContext tenantContext,
    ICorrelationContext correlationContext,
    IClock clock) : IOutbox
{
    public void Enqueue(IIntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        var type = integrationEvent.GetType();

        context.OutboxMessages.Add(new OutboxMessage
        {
            // The event's own id, not a fresh one: a consumer that deduplicates on EventId and an
            // operator reading the outbox table must be looking at the same value.
            Id = integrationEvent.EventId,
            TenantId = tenantContext.TenantId,
            Type = OutboxSerialization.NameOf(type),
            Payload = JsonSerializer.Serialize(integrationEvent, type, OutboxSerialization.Options),
            OccurredAt = integrationEvent.OccurredAtUtc == default
                ? clock.UtcNow
                : integrationEvent.OccurredAtUtc,
            CorrelationId = integrationEvent.CorrelationId ?? correlationContext.CorrelationId,
        });
    }
}
