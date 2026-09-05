using KlaraHome.Contracts.Platform;
using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Guards;
using KlaraHome.SharedKernel.Primitives;

namespace KlaraHome.Modules.Platform.Domain;

/// <summary>
/// One immutable record of a privileged or money-affecting action
/// (docs/07-security-compliance.md §7).
/// </summary>
/// <remarks>
/// <para>
/// There is no method to change one. That is not an oversight to be tidied up later: an audit row
/// that can be edited is not an audit row, and the same rule is enforced in the database by a
/// trigger that rejects <c>UPDATE</c> and <c>DELETE</c> outright.
/// </para>
/// <para>
/// The key is <c>(occurred_at, id)</c> rather than <c>id</c> alone. The table is partitioned by
/// month on <c>occurred_at</c>, and PostgreSQL requires the partition key to appear in every
/// unique constraint.
/// </para>
/// <para>
/// It is <see cref="IAppendOnly"/>, which keeps the <c>xmin</c> concurrency token off it. That is
/// not a tidiness choice: PostgreSQL refuses to return a system column from a partitioned table, so
/// the <c>INSERT ... RETURNING xmin</c> the convention would otherwise produce fails outright.
/// </para>
/// </remarks>
internal sealed class AuditLogEntry : ITenantScoped, IAppendOnly
{
    private AuditLogEntry(Guid id, DateTimeOffset occurredAt, string action, string entityType)
    {
        Id = id;
        OccurredAt = occurredAt;
        Action = Guard.NotNullOrWhiteSpace(action);
        EntityType = Guard.NotNullOrWhiteSpace(entityType);
    }

    /// <summary>Required by EF Core's materialiser.</summary>
    private AuditLogEntry()
    {
        Action = string.Empty;
        EntityType = string.Empty;
    }

    /// <summary>Identity of the entry. UUIDv7, so ordering by id matches ordering by time.</summary>
    public Guid Id { get; private set; }

    /// <summary>When the action happened. The partition key.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; private set; }

    /// <summary>The acting subject, or null when nobody was signed in.</summary>
    public Guid? ActorId { get; private set; }

    /// <summary>What class of actor did it.</summary>
    public AuditActorType ActorType { get; private set; }

    /// <summary>The dotted verb phrase describing what was done.</summary>
    public string Action { get; private set; }

    /// <summary>The kind of thing acted on.</summary>
    public string EntityType { get; private set; }

    /// <summary>Identifier of the thing acted on, as a string; not every subject has a GUID.</summary>
    public string? EntityId { get; private set; }

    /// <summary>State before the change, as JSON. Null for a creation.</summary>
    public string? Before { get; private set; }

    /// <summary>State after the change, as JSON. Null for a deletion.</summary>
    public string? After { get; private set; }

    /// <summary>Client IP the action arrived from, when it came over HTTP.</summary>
    public string? Ip { get; private set; }

    /// <summary>Client user agent, when it came over HTTP.</summary>
    public string? UserAgent { get; private set; }

    /// <summary>Correlation id of the request that caused it, so logs and audit line up.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Builds an entry. Every field is supplied at once because none of them may change later.</summary>
    /// <param name="occurredAt">When it happened.</param>
    /// <param name="action">The dotted verb phrase.</param>
    /// <param name="entityType">The kind of thing acted on.</param>
    /// <param name="entityId">Identifier of the thing acted on.</param>
    /// <param name="actorType">What class of actor did it.</param>
    /// <param name="actorId">The acting subject, if any.</param>
    /// <param name="before">State before, as JSON.</param>
    /// <param name="after">State after, as JSON.</param>
    /// <param name="ip">Client IP.</param>
    /// <param name="userAgent">Client user agent.</param>
    /// <param name="correlationId">Correlation id of the causing request.</param>
    public static AuditLogEntry Record(
        DateTimeOffset occurredAt,
        string action,
        string entityType,
        string? entityId,
        AuditActorType actorType,
        Guid? actorId,
        string? before,
        string? after,
        string? ip,
        string? userAgent,
        string? correlationId)
        => new(UuidV7.NewAt(occurredAt), occurredAt, action, entityType)
        {
            EntityId = entityId,
            ActorType = actorType,
            ActorId = actorId,
            Before = before,
            After = after,
            Ip = ip,
            UserAgent = userAgent,
            CorrelationId = correlationId,
        };
}
