using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Payments.Infrastructure.Persistence;

/// <summary>
/// The Payments module's data access.
/// </summary>
/// <remarks>
/// <para>
/// The one schema in this platform with <b>no vendor scope at all</b>, and that is a decision rather
/// than an omission. A payment is made by a shopper against an <em>order</em>, which may span two
/// sellers; there is no seller a payment belongs to, and inventing one would either split a
/// collection that was never split or show one seller what another was paid.
/// </para>
/// <para>
/// What a seller may see about money is their settlement — their share, after commission, of the
/// orders they fulfilled — and that is Step 18's, in its own schema, derived from these rows rather
/// than joined to them.
/// </para>
/// <para>
/// The consequence is that every read here is a platform read, gated by permission. There is no
/// vendor surface in this module, and any future one must go through Settlements.
/// </para>
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="callerContext">The current caller.</param>
internal sealed class PaymentsDbContext(
    DbContextOptions<PaymentsDbContext> options,
    ITenantContext tenantContext,
    ICallerContext? callerContext = null)
    : KlaraHomeDbContext(options, tenantContext, callerContext)
{
    /// <inheritdoc />
    public override string Schema => PaymentsModule.SchemaName;

    /// <summary>Collections against orders.</summary>
    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>Every try at collecting, including the ones that failed.</summary>
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();

    /// <summary>Money going back out.</summary>
    public DbSet<Refund> Refunds => Set<Refund>();

    /// <summary>Every webhook, exactly as it arrived. Replay index and dead-letter queue in one.</summary>
    public DbSet<GatewayEvent> GatewayEvents => Set<GatewayEvent>();

    /// <summary>Cash owed and collected at doors.</summary>
    public DbSet<CodCollection> CodCollections => Set<CodCollection>();

    /// <summary>Imported settlement reports.</summary>
    public DbSet<GatewaySettlement> GatewaySettlements => Set<GatewaySettlement>();

    /// <summary>Their lines, which is where a mismatch is addressable.</summary>
    public DbSet<GatewaySettlementEntry> GatewaySettlementEntries => Set<GatewaySettlementEntry>();

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new RefundConfiguration());
        modelBuilder.ApplyConfiguration(new GatewayEventConfiguration());
        modelBuilder.ApplyConfiguration(new CodCollectionConfiguration());
        modelBuilder.ApplyConfiguration(new GatewaySettlementConfiguration());
        modelBuilder.ApplyConfiguration(new GatewaySettlementEntryConfiguration());
    }
}
