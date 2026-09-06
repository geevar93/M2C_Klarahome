using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Carts.Domain;
using KlaraHome.Modules.Carts.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Carts.Infrastructure.Persistence;

/// <summary>
/// The Cart module's data access.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is vendor-scoped, and that is deliberate rather than an omission. A basket is a
/// shopper's, not a seller's: a multi-vendor cart belongs to none of the sellers in it, and giving
/// these tables a vendor column would let a seller read what a shopper is buying from their
/// competitors.
/// </para>
/// <para>
/// The line does carry a <c>vendor_id</c> — but as a grouping key copied from the offer, not as a
/// scope. Per-seller views of demand are Reporting's, from the order, and are aggregate.
/// </para>
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="callerContext">The current caller.</param>
internal sealed class CartsDbContext(
    DbContextOptions<CartsDbContext> options,
    ITenantContext tenantContext,
    ICallerContext? callerContext = null)
    : KlaraHomeDbContext(options, tenantContext, callerContext)
{
    /// <inheritdoc />
    public override string Schema => CartsModule.SchemaName;

    /// <summary>Shoppers' baskets.</summary>
    public DbSet<Cart> Carts => Set<Cart>();

    /// <summary>The offers in them.</summary>
    public DbSet<CartLine> CartLines => Set<CartLine>();

    /// <summary>Runs at paying for a basket.</summary>
    public DbSet<CheckoutSession> CheckoutSessions => Set<CheckoutSession>();

    /// <summary>The per-seller delivery choices on them.</summary>
    public DbSet<CheckoutShipment> CheckoutShipments => Set<CheckoutShipment>();

    /// <summary>Every use of an idempotency key against a checkout.</summary>
    public DbSet<CheckoutPlacement> CheckoutPlacements => Set<CheckoutPlacement>();

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new CartConfiguration());
        modelBuilder.ApplyConfiguration(new CartLineConfiguration());
        modelBuilder.ApplyConfiguration(new CheckoutSessionConfiguration());
        modelBuilder.ApplyConfiguration(new CheckoutShipmentConfiguration());
        modelBuilder.ApplyConfiguration(new CheckoutPlacementConfiguration());
    }
}
