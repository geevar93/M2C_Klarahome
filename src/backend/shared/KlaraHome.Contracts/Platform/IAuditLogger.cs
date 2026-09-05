namespace KlaraHome.Contracts.Platform;

/// <summary>
/// Records a privileged or money-affecting action in the immutable audit trail
/// (docs/07-security-compliance.md §7).
/// </summary>
/// <remarks>
/// <para>
/// The caller supplies only what it knows: what was done, to what, and what the thing looked like
/// before and after. Actor, IP address, user agent, correlation id, tenant and timestamp are
/// filled in from the ambient request context, because a caller that has to remember them is a
/// caller that will eventually forget one.
/// </para>
/// <para>
/// The entry is written in its own transaction, after the change it describes has been committed.
/// An audit row is a record of something that happened, so recording one for a change that was
/// then rolled back would be worse than the narrow window in which a crash loses the row.
/// </para>
/// </remarks>
public interface IAuditLogger
{
    /// <summary>Appends one entry. Never throws for a caller error; validation is a programming error.</summary>
    /// <param name="entry">What happened.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>One thing that happened, as the caller knows it.</summary>
public sealed record AuditEntry
{
    /// <summary>
    /// What was done, as a stable dotted verb phrase: <c>platform.settings.updated</c>,
    /// <c>orders.sub-order.cancelled</c>. Queried on, so it must not be a sentence.
    /// </summary>
    public required string Action { get; init; }

    /// <summary>The kind of thing acted on, for example <c>StoreSetting</c> or <c>SubOrder</c>.</summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// Identifier of the thing acted on. A string rather than a <see cref="Guid"/> because not
    /// every audited subject has one — a settings section is identified by its key.
    /// </summary>
    public string? EntityId { get; init; }

    /// <summary>State before the change. Serialised to <c>jsonb</c>; null for a creation.</summary>
    public object? Before { get; init; }

    /// <summary>State after the change. Serialised to <c>jsonb</c>; null for a deletion.</summary>
    public object? After { get; init; }

    /// <summary>
    /// What kind of actor did it. Defaults to <see cref="AuditActorType.System"/>; the Identity
    /// module supplies the real value from Step 7.
    /// </summary>
    public AuditActorType ActorType { get; init; } = AuditActorType.System;

    /// <summary>
    /// The acting subject, when the caller knows better than the ambient user context — an
    /// impersonated session, or a job acting on a specific user's behalf.
    /// </summary>
    public Guid? ActorId { get; init; }
}

/// <summary>The class of actor behind an audited action.</summary>
public enum AuditActorType
{
    /// <summary>A background job, a migration or a seeder. Nobody to attribute it to.</summary>
    System = 0,

    /// <summary>An unauthenticated caller — a failed login, a public form submission.</summary>
    Anonymous = 1,

    /// <summary>A shopper acting on their own account.</summary>
    Customer = 2,

    /// <summary>A seller's staff user, acting within that vendor's scope.</summary>
    VendorUser = 3,

    /// <summary>Platform staff, including administrators.</summary>
    StaffUser = 4,
}
