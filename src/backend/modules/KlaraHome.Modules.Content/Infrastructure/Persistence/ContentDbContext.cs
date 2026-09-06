using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Content.Infrastructure.Persistence;

/// <summary>
/// The Content module's data access.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is vendor-scoped, and unlike the search index that is not a nuance — it is a
/// statement about what this module is. The storefront belongs to the platform: a seller does not
/// merchandise the home page, does not write the returns policy and does not decide which collection
/// appears above the fold. Every table in this schema is platform-wide, and a vendor caller has no
/// business in any of them.
/// </para>
/// <para>
/// It owns everything in it, which makes it the opposite of the module before it. Search can be
/// rebuilt from the catalogue at any time; a page nobody backed up is gone. That is why the version
/// history is append-only and why deletion is soft everywhere it exists here.
/// </para>
/// <para>
/// The one thing it does not own is products. A collection holds product ids and a carousel names
/// them, and every fact about them — the picture, the price, the seller — is resolved through the
/// catalogue's contracts at read time. There is no foreign key, because there cannot be one across a
/// schema (docs/01-architecture.md §2.1), and a product that has since been archived simply drops out
/// of the card list rather than breaking the page.
/// </para>
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="callerContext">The current caller, read by the vendor query filter.</param>
internal sealed class ContentDbContext(
    DbContextOptions<ContentDbContext> options,
    ITenantContext tenantContext,
    ICallerContext? callerContext = null)
    : KlaraHomeDbContext(options, tenantContext, callerContext)
{
    /// <inheritdoc />
    public override string Schema => ContentModule.SchemaName;

    /// <summary>The pages, each an ordered list of typed blocks.</summary>
    public DbSet<ContentPage> Pages => Set<ContentPage>();

    /// <summary>Their blocks. Never queried on their own; reached through a page.</summary>
    public DbSet<ContentBlock> Blocks => Set<ContentBlock>();

    /// <summary>Every page as it stood at a publish, for preview and rollback.</summary>
    public DbSet<PageVersion> PageVersions => Set<PageVersion>();

    /// <summary>The navigation menus and the footer.</summary>
    public DbSet<Menu> Menus => Set<Menu>();

    /// <summary>Their items.</summary>
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();

    /// <summary>The scheduled, targeted promotional slots.</summary>
    public DbSet<Banner> Banners => Set<Banner>();

    /// <summary>The curated groups of products.</summary>
    public DbSet<ProductCollection> Collections => Set<ProductCollection>();

    /// <summary>What is in them.</summary>
    public DbSet<CollectionItem> CollectionItems => Set<CollectionItem>();

    /// <summary>The redirect manager's rules.</summary>
    public DbSet<Redirect> Redirects => Set<Redirect>();

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new ContentPageConfiguration());
        modelBuilder.ApplyConfiguration(new ContentBlockConfiguration());
        modelBuilder.ApplyConfiguration(new PageVersionConfiguration());
        modelBuilder.ApplyConfiguration(new MenuConfiguration());
        modelBuilder.ApplyConfiguration(new MenuItemConfiguration());
        modelBuilder.ApplyConfiguration(new BannerConfiguration());
        modelBuilder.ApplyConfiguration(new ProductCollectionConfiguration());
        modelBuilder.ApplyConfiguration(new CollectionItemConfiguration());
        modelBuilder.ApplyConfiguration(new RedirectConfiguration());
    }
}
