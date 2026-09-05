namespace KlaraHome.Infrastructure.Tenancy;

/// <summary>
/// The tenant the current operation belongs to. Every row written carries it and every query
/// is filtered by it, so a handler can neither forget to set it nor read across it.
/// </summary>
/// <remarks>
/// Step 4 resolves this from configuration — one deployment, one tenant. Step 6 replaces the
/// implementation with a database-backed tenant resolved from the request. Nothing that depends
/// on this interface changes when it does; that is the point of the indirection.
/// </remarks>
public interface ITenantContext
{
    /// <summary>The tenant's primary key, as stored in <c>tenant_id</c> on every table.</summary>
    Guid TenantId { get; }

    /// <summary>The tenant's short code (ADR-006), for logs and diagnostics.</summary>
    string Code { get; }
}

/// <summary>
/// The user the current operation is attributed to, or <see langword="null"/> for anonymous and
/// background work. Used by the auditing interceptor to fill <c>created_by</c>/<c>updated_by</c>.
/// </summary>
/// <remarks>
/// The Identity module issues the <c>sub</c> claim this reads, from Step 7. Until then every
/// operation is unattributed, which is correct rather than merely tolerable: there is no one to
/// attribute it to.
/// </remarks>
public interface IUserContext
{
    /// <summary>The authenticated subject, when there is one.</summary>
    Guid? UserId { get; }
}
