using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Settlements.Domain;
using KlaraHome.Modules.Settlements.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Settlements.Infrastructure.Persistence;

/// <summary>
/// The Settlements module's data access.
/// </summary>
/// <remarks>
/// <para>
/// Almost everything here is vendor-scoped, which is unusual and is the shape of the problem: a
/// settlement schema is, table for table, a set of accounts belonging to sellers. A seller reading
/// their own statement and finance reading the whole ledger run the same queries, and the vendor
/// query filter is the only difference between them.
/// </para>
/// <para>
/// The batch is the exception. A payout run spans sellers, so it carries no vendor scope of its own
/// and its items carry theirs — which means a seller can see the line that paid them without seeing
/// the run that other sellers were paid in.
/// </para>
/// <para>
/// The counter table has no vendor scope either, and that is not an oversight. It is taken with
/// <c>SELECT ... FOR UPDATE</c>, and a lock taken inside a query filter's subquery is a lock this
/// code would be trusting the provider to preserve.
/// </para>
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="callerContext">The current caller.</param>
internal sealed class SettlementsDbContext(
    DbContextOptions<SettlementsDbContext> options,
    ITenantContext tenantContext,
    ICallerContext? callerContext = null)
    : KlaraHomeDbContext(options, tenantContext, callerContext)
{
    /// <inheritdoc />
    public override string Schema => SettlementsModule.SchemaName;

    /// <summary>Every movement on every seller's account. Append-only.</summary>
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    /// <summary>The periods those movements are drawn into.</summary>
    public DbSet<SettlementCycle> Cycles => Set<SettlementCycle>();

    /// <summary>The runs that send the money.</summary>
    public DbSet<PayoutBatch> PayoutBatches => Set<PayoutBatch>();

    /// <summary>One seller's transfer within a run.</summary>
    public DbSet<PayoutItem> PayoutItems => Set<PayoutItem>();

    /// <summary>The platform's own tax invoices, one per closed cycle.</summary>
    public DbSet<CommissionInvoice> CommissionInvoices => Set<CommissionInvoice>();

    /// <summary>The gapless counter behind a payout reference and an invoice number.</summary>
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new LedgerEntryConfiguration());
        modelBuilder.ApplyConfiguration(new SettlementCycleConfiguration());
        modelBuilder.ApplyConfiguration(new PayoutBatchConfiguration());
        modelBuilder.ApplyConfiguration(new PayoutItemConfiguration());
        modelBuilder.ApplyConfiguration(new CommissionInvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new NumberSequenceConfiguration());
    }
}
