namespace KlaraHome.SharedKernel.Domain;

/// <summary>
/// A row that belongs to exactly one tenant. Every business table carries this
/// (docs/03-database-design.md §1); the EF layer indexes on it, filters on it globally, and
/// stamps it on insert, so no handler ever has to remember to.
/// </summary>
/// <remarks>
/// Deliberately a persistence-agnostic marker: the SharedKernel is referenced from module Domain
/// layers, which must not see EF Core at all.
/// </remarks>
public interface ITenantScoped
{
    /// <summary>The owning tenant. Assigned by the persistence layer on insert.</summary>
    Guid TenantId { get; }
}

/// <summary>
/// A row whose creation and last modification are recorded. Populated by the auditing
/// interceptor, never by application code — see docs/03-database-design.md §1.
/// </summary>
/// <remarks>
/// This is the cheap "who touched this row last" record. It is not the audit trail: the full
/// before/after history lives in <c>platform.audit_logs</c> and arrives with Step 6.
/// </remarks>
public interface IAuditable
{
    /// <summary>When the row was inserted, in UTC.</summary>
    DateTimeOffset CreatedAt { get; }

    /// <summary>The user who inserted it, when the operation had a principal.</summary>
    Guid? CreatedBy { get; }

    /// <summary>When the row was last updated, in UTC. Null until the first update.</summary>
    DateTimeOffset? UpdatedAt { get; }

    /// <summary>The user who last updated it, when the operation had a principal.</summary>
    Guid? UpdatedBy { get; }
}

/// <summary>
/// A row that is retired rather than removed, because its history matters — products, listings,
/// categories, users and CMS content (docs/03-database-design.md §1). Everything else is hard
/// deleted; implementing this interface is a deliberate decision, not a default.
/// </summary>
/// <remarks>
/// A global query filter hides deleted rows from every query, so a soft-deleted row behaves as
/// though it were gone. Reading them back is an explicit opt-in.
/// </remarks>
public interface ISoftDeletable
{
    /// <summary>When the row was retired, in UTC. Null while the row is live.</summary>
    DateTimeOffset? DeletedAt { get; }

    /// <summary>The user who retired it, when the operation had a principal.</summary>
    Guid? DeletedBy { get; }
}

/// <summary>
/// A row that is written once and never changed. Marks an entity out of the optimistic-concurrency
/// convention: there is no lost update to detect when nothing ever updates.
/// </summary>
/// <remarks>
/// <para>
/// The audit trail is the first of these, and it is also why the marker has to exist rather than
/// simply being a rule nobody breaks. Its table is partitioned by month, and PostgreSQL refuses to
/// return a system column from a partitioned table — so an <c>INSERT ... RETURNING xmin</c>, which
/// is what mapping <c>xmin</c> as a concurrency token produces, fails outright with
/// <c>0A000: cannot retrieve a system column in this context</c>.
/// </para>
/// <para>
/// Applying this to a row that <em>is</em> updated would silently remove its protection against a
/// lost update, so it is a deliberate declaration and never a default.
/// </para>
/// </remarks>
public interface IAppendOnly;
