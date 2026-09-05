using KlaraHome.SharedKernel.Domain;

namespace KlaraHome.Infrastructure.Persistence.Outbox;

/// <summary>
/// An integration event that has been decided but not yet announced. Written in the same
/// transaction as the state change that produced it, so the fact and the notice of the fact
/// cannot disagree: either both commit or neither does (ADR-003).
/// </summary>
/// <remarks>
/// This is deliberately not a domain entity — it is a queue row. It has no behaviour, no
/// invariants of its own and no aggregate; the dispatcher owns its lifecycle.
/// </remarks>
public sealed class OutboxMessage : ITenantScoped
{
    /// <summary>The message's identity. UUIDv7, so polling reads in occurrence order.</summary>
    public Guid Id { get; init; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>
    /// The assembly-qualified-free contract name (<c>KlaraHome.Contracts.*</c>), used to resolve
    /// the handler. A name rather than a CLR type so a renamed class is a visible migration
    /// problem instead of a silent one.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>The serialised event, stored as <c>jsonb</c>.</summary>
    public required string Payload { get; init; }

    /// <summary>When the fact occurred, in UTC.</summary>
    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>When every handler completed. Null while the message is pending.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>How many dispatch attempts have been made. Caps the retry budget.</summary>
    public int Attempts { get; set; }

    /// <summary>The last failure, kept so a stuck message can be diagnosed without re-running it.</summary>
    public string? Error { get; set; }

    /// <summary>The correlation id of the request that produced the event, carried to its handlers.</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>
/// A record that a message has already been handled. The outbox guarantees at-least-once
/// delivery; this is what turns that into effectively-once for the handler
/// (docs/03-database-design.md §4.1).
/// </summary>
public sealed class InboxMessage
{
    /// <summary>The originating <see cref="OutboxMessage.Id"/>.</summary>
    public Guid MessageId { get; init; }

    /// <summary>The handler that consumed it. Two handlers of one message are two rows.</summary>
    public required string Handler { get; init; }

    /// <summary>When the handler completed, in UTC.</summary>
    public DateTimeOffset ProcessedAt { get; init; }
}
