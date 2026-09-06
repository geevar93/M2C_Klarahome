using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Infrastructure.Persistence;

/// <summary>
/// The Catalog module's data access.
/// </summary>
/// <remarks>
/// Three of its tables are <see cref="KlaraHome.SharedKernel.Domain.IVendorScoped"/> and carry a
/// nullable seller: a product or a listing may belong to a seller, or to the platform. The global
/// vendor filter therefore does something subtle here — a vendor caller sees their own rows
/// <em>and</em> the platform's shared products, which is exactly the shared-catalogue behaviour a
/// marketplace needs, and is why those columns are nullable rather than required.
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="callerContext">The current caller, read by the vendor query filter.</param>
internal sealed class CatalogDbContext(
    DbContextOptions<CatalogDbContext> options,
    ITenantContext tenantContext,
    ICallerContext? callerContext = null)
    : KlaraHomeDbContext(options, tenantContext, callerContext)
{
    /// <inheritdoc />
    public override string Schema => CatalogModule.SchemaName;

    /// <summary>The browse tree.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>The brands this catalogue carries.</summary>
    public DbSet<Brand> Brands => Set<Brand>();

    /// <summary>Every property a product or variant can be described by.</summary>
    public DbSet<ProductAttribute> Attributes => Set<ProductAttribute>();

    /// <summary>The permitted values of the list-typed attributes.</summary>
    public DbSet<AttributeOption> AttributeOptions => Set<AttributeOption>();

    /// <summary>The named bundles a category imposes on its products.</summary>
    public DbSet<AttributeSet> AttributeSets => Set<AttributeSet>();

    /// <summary>Which attributes are in which set.</summary>
    public DbSet<AttributeSetMember> AttributeSetMembers => Set<AttributeSetMember>();

    /// <summary>The marketable concepts.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>Their described properties.</summary>
    public DbSet<ProductAttributeValue> ProductAttributeValues => Set<ProductAttributeValue>();

    /// <summary>The sellable things. Stock and offers attach here.</summary>
    public DbSet<Variant> Variants => Set<Variant>();

    /// <summary>The defining axes of each variant.</summary>
    public DbSet<VariantAttributeValue> VariantAttributeValues => Set<VariantAttributeValue>();

    /// <summary>The vendors' offers. This is the <c>listing_id</c> six other schemas hold.</summary>
    public DbSet<Listing> Listings => Set<Listing>();

    /// <summary>Product and variant galleries.</summary>
    public DbSet<CatalogMediaAsset> MediaAssets => Set<CatalogMediaAsset>();

    /// <summary>Every trip a product has made through the moderation queue.</summary>
    public DbSet<ProductModeration> Moderations => Set<ProductModeration>();

    /// <summary>Bulk imports and exports, and the reports they produced.</summary>
    public DbSet<CatalogJob> Jobs => Set<CatalogJob>();

    /// <summary>
    /// The sequence behind a generated SKU.
    /// </summary>
    /// <remarks>
    /// A database sequence rather than a count of rows, for the same reason the vendor code uses
    /// one: a count is a race. It is not a primary key and it is not an invoice number, so the gap
    /// a rolled-back transaction leaves costs nothing.
    /// </remarks>
    public const string SkuSequenceName = "sku_seq";

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasSequence<long>(SkuSequenceName, CatalogModule.SchemaName).StartsAt(1).IncrementsBy(1);

        modelBuilder.ApplyConfiguration(new CategoryConfiguration());
        modelBuilder.ApplyConfiguration(new BrandConfiguration());
        modelBuilder.ApplyConfiguration(new ProductAttributeConfiguration());
        modelBuilder.ApplyConfiguration(new AttributeOptionConfiguration());
        modelBuilder.ApplyConfiguration(new AttributeSetConfiguration());
        modelBuilder.ApplyConfiguration(new AttributeSetMemberConfiguration());
        modelBuilder.ApplyConfiguration(new ProductConfiguration());
        modelBuilder.ApplyConfiguration(new ProductAttributeValueConfiguration());
        modelBuilder.ApplyConfiguration(new VariantConfiguration());
        modelBuilder.ApplyConfiguration(new VariantAttributeValueConfiguration());
        modelBuilder.ApplyConfiguration(new ListingConfiguration());
        modelBuilder.ApplyConfiguration(new CatalogMediaAssetConfiguration());
        modelBuilder.ApplyConfiguration(new ProductModerationConfiguration());
        modelBuilder.ApplyConfiguration(new CatalogJobConfiguration());
    }
}
