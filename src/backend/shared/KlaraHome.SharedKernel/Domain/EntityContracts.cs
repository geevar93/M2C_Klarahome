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

/// <summary>
/// A row that belongs to exactly one seller. Vendor staff may only ever read and write their own
/// vendor's rows (docs/07-security-compliance.md §2), and that is enforced by a global query
/// filter in the data layer rather than by a check in each handler — a developer who forgets the
/// filter gets no data instead of everyone's.
/// </summary>
/// <remarks>
/// <para>
/// The filter is a no-op for a caller with no vendor scope: platform staff and background work
/// see every vendor's rows, which is the whole point of the distinction. The nullable column
/// exists because some vendor-scoped tables also hold platform-wide rows — a staff role
/// assignment carries no vendor.
/// </para>
/// <para>
/// Like <see cref="ITenantScoped"/> this is deliberately persistence-agnostic: a module's Domain
/// layer must not see EF Core.
/// </para>
/// </remarks>
public interface IVendorScoped
{
    /// <summary>The owning seller, or null for a row that belongs to the platform itself.</summary>
    Guid? VendorId { get; }
}

/// <summary>
/// A vendor-scoped table whose <em>platform-owned</em> rows are readable by every seller.
/// </summary>
/// <remarks>
/// <para>
/// The default for <see cref="IVendorScoped"/> is that a seller sees their own rows and nothing
/// else, platform-owned rows included: a staff role assignment or a platform warehouse is none of
/// their business. A shared catalogue is the exception the marketplace model is built on — the
/// platform publishes a product and several sellers offer against it, which they cannot do if they
/// cannot see it — so the table that holds it says so here, explicitly, rather than the rule being
/// widened for everything.
/// </para>
/// <para>
/// This widens <b>reads</b> only. Whether a caller may write to a row they can see is a separate
/// question the owning module answers — <c>CatalogScope.CanWrite</c> is the whole of it for the
/// catalogue — because "readable by all, writable by its owner" is not something a query filter can
/// express.
/// </para>
/// </remarks>
public interface IPlatformShared;

/// <summary>
/// A row in a PostgreSQL <em>partitioned</em> table. Declaring it removes the optimistic-concurrency
/// convention, because the database cannot supply one: PostgreSQL refuses to return a system column
/// from a partitioned table, so an <c>INSERT ... RETURNING xmin</c> — which is what mapping
/// <c>xmin</c> as a concurrency token produces — fails outright with
/// <c>0A000: cannot retrieve a system column in this context</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IAppendOnly"/> covers the same ground for a table that is never updated, and the audit
/// trail is both. This marker exists for the table that is partitioned <em>and</em> updated —
/// <c>notifications.notification_messages</c>, whose rows move from queued to sent — where the
/// append-only marker would be a lie.
/// </para>
/// <para>
/// A partitioned, updatable table needs a different answer to the lost-update problem than a
/// version column. The notification queue's answer is pessimistic: rows are claimed with
/// <c>FOR UPDATE SKIP LOCKED</c>, so two dispatchers never hold the same row and there is no
/// concurrent update to lose.
/// </para>
/// </remarks>
public interface IPartitioned;
