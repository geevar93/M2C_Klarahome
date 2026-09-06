using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Reporting.Domain;
using KlaraHome.Modules.Reporting.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Reporting.Infrastructure.Persistence;

/// <summary>
/// The Reporting module's data access.
/// </summary>
/// <remarks>
/// <para>
/// Seven fact tables and two for the export machinery, and there is deliberately nothing else. No
/// rollups and no materialised views: every fact row is already one transactional row with the
/// category, the seller and the payment method denormalised onto it, so a report is a filtered
/// aggregation over one table with no join anywhere in the module (ADR-021). That shape is what
/// makes the numbers reconcilable — a figure is a sum over rows that each correspond to something
/// that really happened, rather than a sum somebody else computed at three in the morning.
/// </para>
/// <para>
/// Four of the fact tables are vendor-scoped, which means the global query filter confines a seller
/// to their own rows without a single handler having to remember to. That is the strongest control
/// in this module: a seller running "sales by seller" over the wrong filter would be reading their
/// competitors' takings, and a developer who forgets the clause gets no data rather than everyone's.
/// The three that are not scoped — orders, payments and the funnel — are the platform's own, and the
/// reports over them are declared not vendor-scoped so a seller cannot ask for one at all.
/// </para>
/// <para>
/// Nothing in this module writes anywhere else, and nothing outside it writes here. Every row
/// arrives through an integration-event handler or through the daily inventory snapshot, which is
/// what "Reporting is read-only" means in practice: it is read-only about the <em>business</em>,
/// not about its own schema.
/// </para>
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="callerContext">The current caller, read by the vendor query filter.</param>
internal sealed class ReportingDbContext(
    DbContextOptions<ReportingDbContext> options,
    ITenantContext tenantContext,
    ICallerContext? callerContext = null)
    : KlaraHomeDbContext(options, tenantContext, callerContext)
{
    /// <inheritdoc />
    public override string Schema => ReportingModule.SchemaName;

    /// <summary>One row per order, followed through its life.</summary>
    public DbSet<OrderFact> Orders => Set<OrderFact>();

    /// <summary>One row per confirmed order line. The workhorse of the module.</summary>
    public DbSet<SaleLineFact> OrderLines => Set<SaleLineFact>();

    /// <summary>One row per movement of money.</summary>
    public DbSet<PaymentFact> Payments => Set<PaymentFact>();

    /// <summary>One row per returned line.</summary>
    public DbSet<ReturnedLineFact> ReturnLines => Set<ReturnedLineFact>();

    /// <summary>One row per closed settlement period.</summary>
    public DbSet<SettlementFact> Settlements => Set<SettlementFact>();

    /// <summary>One row per observable step of a shopper's journey.</summary>
    public DbSet<FunnelFact> FunnelEvents => Set<FunnelFact>();

    /// <summary>One row per stock line per day. The only scheduled write in the module.</summary>
    public DbSet<InventoryAgeFact> InventoryAgeing => Set<InventoryAgeFact>();

    /// <summary>The standing instructions to produce a report on a timetable.</summary>
    public DbSet<ReportSchedule> Schedules => Set<ReportSchedule>();

    /// <summary>Every production of a report, scheduled or asked for by hand.</summary>
    public DbSet<ReportRun> Runs => Set<ReportRun>();

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new OrderFactConfiguration());
        modelBuilder.ApplyConfiguration(new OrderLineFactConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentFactConfiguration());
        modelBuilder.ApplyConfiguration(new ReturnLineFactConfiguration());
        modelBuilder.ApplyConfiguration(new SettlementFactConfiguration());
        modelBuilder.ApplyConfiguration(new FunnelFactConfiguration());
        modelBuilder.ApplyConfiguration(new InventoryAgeFactConfiguration());
        modelBuilder.ApplyConfiguration(new ReportScheduleConfiguration());
        modelBuilder.ApplyConfiguration(new ReportRunConfiguration());
    }
}
