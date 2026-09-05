namespace KlaraHome.Contracts.IntegrationEvents;

/// <summary>
/// A fact one module publishes for others to react to, asynchronously and reliably. Integration
/// events are written to the transactional outbox in the same transaction as the state change
/// that produced them (ADR-003) and dispatched by the worker.
/// </summary>
/// <remarks>
/// Contract rules: additive changes only, primitives and simple DTOs only, and no reference to
/// any module's internal types. A breaking change means a new event type, not an edit.
/// </remarks>
public interface IIntegrationEvent
{
    /// <summary>Unique id of this occurrence. Consumers use it to deduplicate.</summary>
    Guid EventId { get; }

    /// <summary>When the fact occurred, in UTC.</summary>
    DateTimeOffset OccurredAtUtc { get; }

    /// <summary>Correlation id of the request that caused it, carried across the async boundary.</summary>
    string? CorrelationId { get; }
}
