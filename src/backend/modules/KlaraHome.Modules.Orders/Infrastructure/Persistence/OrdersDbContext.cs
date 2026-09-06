using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Orders.Domain;
using KlaraHome.Modules.Orders.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Orders.Infrastructure.Persistence;

/// <summary>
/// The Ordering module's data access.
/// </summary>
/// <remarks>
/// <para>
/// The one schema in this platform where the vendor scope runs <em>below</em> the aggregate root. An
/// order belongs to a shopper and may span two sellers, so the order itself carries no vendor; the
/// sub-order, its lines and its invoice each do, and the vendor query filter on those three is what
/// lets a seller be handed their own half of a shared basket and nothing of the other half.
/// </para>
/// <para>
/// That asymmetry is deliberate and it has one consequence worth stating: a vendor caller loading an
/// <c>Order</c> gets the order, and the <c>Include</c> of its sub-orders returns only theirs. Every
/// read this module performs for a seller is therefore written against the sub-order, never the
/// order — an order-level total means nothing to a seller who can only see part of it.
/// </para>
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="callerContext">The current caller; supplies the vendor scope.</param>
internal sealed class OrdersDbContext(
    DbContextOptions<OrdersDbContext> options,
    ITenantContext tenantContext,
    ICallerContext? callerContext = null)
    : KlaraHomeDbContext(options, tenantContext, callerContext)
{
    /// <inheritdoc />
    public override string Schema => OrdersModule.SchemaName;

    /// <summary>Orders.</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>One seller's part of an order.</summary>
    public DbSet<SubOrder> SubOrders => Set<SubOrder>();

    /// <summary>The items on them.</summary>
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    /// <summary>The append-only timeline.</summary>
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();

    /// <summary>Sellers' tax invoices.</summary>
    public DbSet<Invoice> Invoices => Set<Invoice>();

    /// <summary>The gapless counters behind order and invoice numbers.</summary>
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new SubOrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderLineConfiguration());
        modelBuilder.ApplyConfiguration(new OrderEventConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new NumberSequenceConfiguration());
    }
}
