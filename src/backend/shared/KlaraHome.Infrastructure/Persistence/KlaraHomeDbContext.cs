using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Infrastructure.Persistence;

/// <summary>
/// The base every module <c>DbContext</c> derives from. It owns exactly one Postgres schema, and
/// nothing in it may reference a table in another (docs/01-architecture.md §2.1) — which is why
/// the schema is a constructor obligation rather than an optional override.
/// </summary>
/// <remarks>
/// <para>
/// Derived contexts stay <c>internal</c> to their module. The host never names one; it registers
/// them through <c>AddModuleDbContext</c> and the migrator resolves them from the descriptors
/// that registration leaves behind.
/// </para>
/// <para>
/// The outbox is mapped here rather than in a module because an integration event must be
/// written in the <em>same transaction</em> as the state change that caused it — that is the
/// entire point of the pattern. The DDL for those tables belongs to whichever context declares
/// <see cref="OwnsMessagingTables"/>; every other context maps them as excluded from migrations,
/// so all modules write to one queue without any of them duplicating its definition.
/// </para>
/// </remarks>
public abstract class KlaraHomeDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    /// <param name="options">Provider options supplied by DI.</param>
    /// <param name="tenantContext">The ambient tenant; stamped on writes and filtered on reads.</param>
    protected KlaraHomeDbContext(DbContextOptions options, ITenantContext tenantContext)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        _tenantContext = tenantContext;
    }

    /// <summary>The Postgres schema this context owns. Must match the module's declared schema.</summary>
    public abstract string Schema { get; }

    /// <summary>
    /// Whether this context carries the migrations for <c>outbox_messages</c> and
    /// <c>inbox_messages</c>. Exactly one context in the solution may return <see langword="true"/>;
    /// the migration runner refuses to start if that is not so.
    /// </summary>
    public virtual bool OwnsMessagingTables => false;

    /// <summary>
    /// The ambient tenant, read by the global query filter. Exposed rather than captured so EF
    /// parameterises it per query instead of baking it into the cached model.
    /// </summary>
    public Guid TenantId => _tenantContext.TenantId;

    /// <summary>Integration events awaiting dispatch, written in the caller's transaction.</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Messages already handled, so a redelivery is a no-op rather than a duplicate.</summary>
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    /// <summary>
    /// Sealed on purpose. A module describes its own tables in <see cref="ConfigureModule"/>; the
    /// conventions that apply to every table in the system are then layered on top, and cannot be
    /// skipped by forgetting to call the base implementation.
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration(OwnsMessagingTables));
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration(OwnsMessagingTables));

        ConfigureModule(modelBuilder);

        modelBuilder.ApplyKlaraHomeConventions(this);
    }

    /// <summary>Where a module maps its own entities. Called before the global conventions.</summary>
    /// <param name="modelBuilder">The model being built.</param>
    protected abstract void ConfigureModule(ModelBuilder modelBuilder);

    /// <summary>
    /// Runs <paramref name="operation"/> inside one transaction, and commits it. This is the only
    /// sanctioned way to span more than one <c>SaveChangesAsync</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retry-on-failure is enabled for every context, and EF refuses a hand-rolled
    /// <c>BeginTransactionAsync</c> under a retrying strategy — it cannot re-run a unit of work
    /// whose boundaries it does not own. The strategy has to wrap the transaction, not the other
    /// way round, so it is done here once instead of being rediscovered by every module.
    /// </para>
    /// <para>
    /// <paramref name="operation"/> may run more than once. It must be safe to retry from the
    /// start: no side effects outside the transaction, and no state carried between attempts.
    /// </para>
    /// </remarks>
    /// <param name="operation">The work to perform; receives this context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExecuteInTransactionAsync(
        Func<KlaraHomeDbContext, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var strategy = Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Database
                .BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);

            await operation(this, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}
