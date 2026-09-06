using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Pricing.Domain;
using KlaraHome.Modules.Pricing.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Pricing.Infrastructure.Persistence;

/// <summary>
/// The Pricing module's data access.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="PriceLists"/> is vendor-scoped, and its seller is nullable: a price list may be
/// the platform's, applying to every offer, or a seller's own. The global vendor filter then shows
/// a vendor caller their own lists and the platform's shared ones and nobody else's.
/// </para>
/// <para>
/// Promotions, tax rates and wallets deliberately are <b>not</b> vendor-scoped. A campaign and a
/// GST rate are the platform's, and a shopper's store credit is the platform's liability to them —
/// none of the three is a seller's to see, and giving them a nullable vendor column would suggest
/// otherwise.
/// </para>
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="callerContext">The current caller, read by the vendor query filter.</param>
internal sealed class PricingDbContext(
    DbContextOptions<PricingDbContext> options,
    ITenantContext tenantContext,
    ICallerContext? callerContext = null)
    : KlaraHomeDbContext(options, tenantContext, callerContext)
{
    /// <inheritdoc />
    public override string Schema => PricingModule.SchemaName;

    /// <summary>Named sets of prices, with their windows and their rank.</summary>
    public DbSet<PriceList> PriceLists => Set<PriceList>();

    /// <summary>The prices in them, one row per offer per quantity tier.</summary>
    public DbSet<PriceListItem> PriceListItems => Set<PriceListItem>();

    /// <summary>The GST payable per HSN code, per period.</summary>
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();

    /// <summary>Coupon codes and automatic cart rules.</summary>
    public DbSet<Promotion> Promotions => Set<Promotion>();

    /// <summary>Which order used which promotion.</summary>
    public DbSet<PromotionRedemption> PromotionRedemptions => Set<PromotionRedemption>();

    /// <summary>Customers' store credit.</summary>
    public DbSet<Wallet> Wallets => Set<Wallet>();

    /// <summary>Every movement of it.</summary>
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new PriceListConfiguration());
        modelBuilder.ApplyConfiguration(new PriceListItemConfiguration());
        modelBuilder.ApplyConfiguration(new TaxRateConfiguration());
        modelBuilder.ApplyConfiguration(new PromotionConfiguration());
        modelBuilder.ApplyConfiguration(new PromotionRedemptionConfiguration());
        modelBuilder.ApplyConfiguration(new WalletConfiguration());
        modelBuilder.ApplyConfiguration(new WalletTransactionConfiguration());
    }
}
