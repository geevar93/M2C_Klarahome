using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Infrastructure.Blocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Content.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>CHECK</c> lists, written once so a column and the constraint on it cannot drift apart.
/// </summary>
/// <remarks>
/// Every enum in this schema is stored as its word and constrained to the words the enum has, which
/// is what docs/03-database-design.md §1 asks for. It is worth more here than in most schemas: a page
/// in a status nothing can move and nothing renders would be invisible until a merchandiser reported
/// that their page had vanished.
/// </remarks>
internal static class ContentCheckConstraints
{
    /// <summary>The values <c>pages.type</c> accepts.</summary>
    public const string PageTypes = "type IN ('Home', 'Landing', 'Static', 'Legal', 'Blog')";

    /// <summary>The values <c>pages.status</c> accepts.</summary>
    public const string PageStatuses =
        "status IN ('Draft', 'InReview', 'Scheduled', 'Published', 'Unpublished', 'Archived')";

    /// <summary>A scheduled page has a time, and nothing else does.</summary>
    /// <remarks>
    /// The invariant the scheduler depends on. Its sweep is "every page that is scheduled and whose
    /// time has passed"; a scheduled page with no time would sit in that state for ever and a
    /// published page with one would be picked up and republished on every pass.
    /// </remarks>
    public const string ScheduleConsistent =
        "(status = 'Scheduled' AND scheduled_at IS NOT NULL) OR (status <> 'Scheduled' AND scheduled_at IS NULL)";

    /// <summary>The values <c>page_blocks.block_type</c> accepts.</summary>
    public const string BlockTypes =
        "block_type IN ('Hero', 'BannerGrid', 'ProductCarousel', 'CategoryTiles', 'RichText', "
        + "'Faq', 'Testimonial', 'CustomHtml')";

    /// <summary>Positions are ordinals.</summary>
    public const string Position = "position >= 0";

    /// <summary>Every window ends after it starts.</summary>
    /// <remarks>
    /// A window that closes before it opens is a block that never renders, and an editor who wrote
    /// one has made a typo in a date field rather than a merchandising decision. It is refused in the
    /// handler too; this is the copy that holds for a row written any other way.
    /// </remarks>
    public const string Window = "starts_at IS NULL OR ends_at IS NULL OR ends_at > starts_at";

    /// <summary>The values <c>menu_items.link_type</c> accepts.</summary>
    public const string MenuLinkTypes = "link_type IN ('None', 'Page', 'Category', 'Collection', 'Url')";

    /// <summary>A link carries what its type needs, and nothing it does not.</summary>
    public const string MenuLinkTarget =
        "(link_type IN ('Page', 'Category', 'Collection') AND target_id IS NOT NULL) "
        + "OR (link_type = 'Url' AND url IS NOT NULL) "
        + "OR (link_type = 'None' AND target_id IS NULL AND url IS NULL)";

    /// <summary>A menu nests no deeper than the storefront renders.</summary>
    public const string MenuDepth = "depth >= 0 AND depth < 3";

    /// <summary>The values <c>banners.placement</c> accepts.</summary>
    public const string BannerPlacements =
        "placement IN ('AnnouncementBar', 'HomeHero', 'HomeStrip', 'CategoryHeader', "
        + "'ListingSidebar', 'ProductStrip', 'CartStrip')";

    /// <summary>
    /// A banner carries the one thing its placement renders.
    /// </summary>
    /// <remarks>
    /// The announcement bar is words and every other placement is a picture, and a row that has
    /// neither is a slot an editor scheduled, believed in, and would never have seen fire.
    /// </remarks>
    public const string BannerContent =
        "(placement = 'AnnouncementBar' AND message IS NOT NULL) "
        + "OR (placement <> 'AnnouncementBar' AND media_file_id IS NOT NULL)";

    /// <summary>An audience is one of the three combinations the flags enum can produce.</summary>
    public const string BannerAudiences = "audience BETWEEN 1 AND 3";

    /// <summary>The values <c>collections.type</c> accepts.</summary>
    public const string CollectionKinds = "type IN ('Manual', 'Rule')";

    /// <summary>Counts are counts.</summary>
    public const string Counts = "item_count >= 0";

    /// <summary>The values <c>redirects.status_code</c> accepts.</summary>
    public const string RedirectStatuses = "status_code IN (301, 302, 410)";

    /// <summary>
    /// A redirect goes somewhere, unless it is a 410, which by definition does not.
    /// </summary>
    public const string RedirectTarget =
        "(status_code = 410 AND to_path IS NULL) OR (status_code <> 410 AND to_path IS NOT NULL)";

    /// <summary>A hit count is a count.</summary>
    public const string HitCount = "hit_count >= 0";

    /// <summary>A snapshot's own number. One-based, matching <c>Guard.Positive</c> in the domain.</summary>
    public const string SnapshotVersion = "version >= 1";

    /// <summary>
    /// The page's snapshot counter, which starts at zero.
    /// </summary>
    /// <remarks>
    /// Zero and one-based at once, because the two columns count different things.
    /// <c>page_versions.version</c> names a snapshot, and there is no snapshot zero.
    /// <c>pages.version</c> counts how many snapshots have been taken, and a page that has never
    /// been published has taken none — <c>NextVersion()</c> is what turns that into the 1 the
    /// first snapshot is written under.
    ///
    /// Both columns are called <c>version</c> and shared one constant, which made every
    /// <c>INSERT</c> into <c>pages</c> fail on <c>ck_pages_version</c>: a page could not be
    /// created at all, and the CMS was unusable from the day the schema landed.
    /// </remarks>
    public const string PageVersionCounter = "version >= 0";
}

/// <summary>Maps <see cref="ContentPage"/> to <c>content.pages</c> and its blocks.</summary>
internal sealed class ContentPageConfiguration : IEntityTypeConfiguration<ContentPage>
{
    public void Configure(EntityTypeBuilder<ContentPage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("pages", table =>
        {
            table.HasCheckConstraint("ck_pages_type", ContentCheckConstraints.PageTypes);
            table.HasCheckConstraint("ck_pages_status", ContentCheckConstraints.PageStatuses);
            table.HasCheckConstraint("ck_pages_schedule", ContentCheckConstraints.ScheduleConsistent);
            table.HasCheckConstraint("ck_pages_version", ContentCheckConstraints.PageVersionCounter);
        });

        builder.HasKey(page => page.Id);
        builder.Property(page => page.Id).ValueGeneratedNever();

        builder.Property(page => page.Slug).HasMaxLength(ContentPage.MaxSlugLength).IsRequired();
        builder.Property(page => page.Title).HasMaxLength(ContentPage.MaxTitleLength).IsRequired();
        builder.Property(page => page.Summary).HasMaxLength(ContentPage.MaxSummaryLength);
        builder.Property(page => page.Author).HasMaxLength(120);

        builder.Property(page => page.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(page => page.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(page => page.Tags).HasColumnType("text[]").IsRequired();

        // jsonb and read whole. Nothing joins to it and nothing filters on it: it is written into a
        // <head> almost exactly as it is, which is the narrow case §1 allows JSON for.
        builder.OwnsOne(page => page.Seo, seo => seo.ToJson());

        builder.HasMany(page => page.Blocks)
            .WithOne()
            .HasForeignKey(block => block.PageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(page => page.Blocks).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The address, and the only uniqueness rule in this schema that has to survive an archive.
        // Filtered on the soft delete rather than on the status, because an archived page keeps its
        // slug in the history and a new page may not take it while the old row is still there.
        builder.HasIndex(page => new { page.TenantId, page.Slug })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        // Exactly one published home page, enforced by the database rather than by the handler that
        // checks for one. Two of them is a storefront rendering whichever row the planner returned
        // first, and a check-then-write in application code is a race that loses it under a
        // simultaneous publish.
        // Keyed on (tenant, type) rather than on the tenant alone so the global convention still adds
        // its own plain tenant index: it skips a table that already has one on exactly that column,
        // and a unique filtered index is not a substitute for it.
        builder.HasIndex(page => new { page.TenantId, page.Type })
            .IsUnique()
            .HasDatabaseName("ux_pages_single_published_home")
            .HasFilter("type = 'Home' AND status = 'Published' AND deleted_at IS NULL");

        // The storefront's read: one page, by address, if it is live.
        builder.HasIndex(page => new { page.TenantId, page.Status, page.Slug });

        // The scheduler's sweep, which runs every minute for ever and must never be a table scan.
        builder.HasIndex(page => new { page.Status, page.ScheduledAt })
            .HasDatabaseName("ix_pages_due")
            .HasFilter("status = 'Scheduled'");

        // The blog index, newest first.
        builder.HasIndex(page => new { page.TenantId, page.Type, page.PublishedAt });
    }
}

/// <summary>Maps <see cref="ContentBlock"/> to <c>content.page_blocks</c>.</summary>
internal sealed class ContentBlockConfiguration : IEntityTypeConfiguration<ContentBlock>
{
    public void Configure(EntityTypeBuilder<ContentBlock> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("page_blocks", table =>
        {
            table.HasCheckConstraint("ck_page_blocks_type", ContentCheckConstraints.BlockTypes);
            table.HasCheckConstraint("ck_page_blocks_position", ContentCheckConstraints.Position);
            table.HasCheckConstraint("ck_page_blocks_window", ContentCheckConstraints.Window);
        });

        builder.HasKey(block => block.Id);
        builder.Property(block => block.Id).ValueGeneratedNever();

        builder.Property(block => block.Type)
            .HasColumnName("block_type")
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(block => block.Config).HasColumnType(ContentJson.ColumnType).IsRequired();

        // Reading a page reads its blocks in order; there is no other access path to this table.
        builder.HasIndex(block => new { block.PageId, block.Position });
    }
}

/// <summary>Maps <see cref="PageVersion"/> to <c>content.page_versions</c>.</summary>
internal sealed class PageVersionConfiguration : IEntityTypeConfiguration<PageVersion>
{
    public void Configure(EntityTypeBuilder<PageVersion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("page_versions", table =>
            table.HasCheckConstraint("ck_page_versions_version", ContentCheckConstraints.SnapshotVersion));

        builder.HasKey(version => version.Id);
        builder.Property(version => version.Id).ValueGeneratedNever();

        builder.Property(version => version.Title).HasMaxLength(ContentPage.MaxTitleLength).IsRequired();
        builder.Property(version => version.Note).HasMaxLength(500);
        builder.Property(version => version.Blocks).HasColumnType(ContentJson.ColumnType).IsRequired();

        builder.OwnsOne(version => version.Seo, seo => seo.ToJson());

        // The history list, newest first, and the lookup a rollback makes. Unique because a second
        // row claiming version 4 would make "roll back to 4" ambiguous, which is the one thing this
        // table exists to prevent.
        builder.HasIndex(version => new { version.PageId, version.Version }).IsUnique();
    }
}

/// <summary>Maps <see cref="Menu"/> to <c>content.menus</c> and its items.</summary>
internal sealed class MenuConfiguration : IEntityTypeConfiguration<Menu>
{
    public void Configure(EntityTypeBuilder<Menu> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("menus");

        builder.HasKey(menu => menu.Id);
        builder.Property(menu => menu.Id).ValueGeneratedNever();

        builder.Property(menu => menu.Code).HasMaxLength(Menu.MaxCodeLength).IsRequired();
        builder.Property(menu => menu.Name).HasMaxLength(Menu.MaxNameLength).IsRequired();
        builder.Property(menu => menu.Placement).HasMaxLength(40);

        builder.HasMany(menu => menu.Items)
            .WithOne()
            .HasForeignKey(item => item.MenuId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(menu => menu.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

        // The code is what the storefront asks for, so it is the key in every sense but the primary
        // one.
        builder.HasIndex(menu => new { menu.TenantId, menu.Code }).IsUnique();
    }
}

/// <summary>Maps <see cref="MenuItem"/> to <c>content.menu_items</c>.</summary>
internal sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("menu_items", table =>
        {
            table.HasCheckConstraint("ck_menu_items_link_type", ContentCheckConstraints.MenuLinkTypes);
            table.HasCheckConstraint("ck_menu_items_link_target", ContentCheckConstraints.MenuLinkTarget);
            table.HasCheckConstraint("ck_menu_items_depth", ContentCheckConstraints.MenuDepth);
            table.HasCheckConstraint("ck_menu_items_position", ContentCheckConstraints.Position);
        });

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();

        builder.Property(item => item.Label).HasMaxLength(MenuItem.MaxLabelLength).IsRequired();
        builder.Property(item => item.Url).HasMaxLength(MenuItem.MaxUrlLength);
        builder.Property(item => item.Badge).HasMaxLength(40);

        builder.Property(item => item.LinkType).HasConversion<string>().HasMaxLength(20).IsRequired();

        // A menu is read whole, in one query, in this order.
        builder.HasIndex(item => new { item.MenuId, item.ParentId, item.Position });
    }
}

/// <summary>Maps <see cref="Banner"/> to <c>content.banners</c>.</summary>
internal sealed class BannerConfiguration : IEntityTypeConfiguration<Banner>
{
    public void Configure(EntityTypeBuilder<Banner> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("banners", table =>
        {
            table.HasCheckConstraint("ck_banners_placement", ContentCheckConstraints.BannerPlacements);
            table.HasCheckConstraint("ck_banners_content", ContentCheckConstraints.BannerContent);
            table.HasCheckConstraint("ck_banners_audience", ContentCheckConstraints.BannerAudiences);
            table.HasCheckConstraint("ck_banners_window", ContentCheckConstraints.Window);
        });

        builder.HasKey(banner => banner.Id);
        builder.Property(banner => banner.Id).ValueGeneratedNever();

        builder.Property(banner => banner.Name).HasMaxLength(Banner.MaxNameLength).IsRequired();
        builder.Property(banner => banner.Message).HasMaxLength(Banner.MaxMessageLength);
        builder.Property(banner => banner.AltText).HasMaxLength(Banner.MaxAltTextLength);
        builder.Property(banner => banner.Link).HasMaxLength(Banner.MaxLinkLength);
        builder.Property(banner => banner.CtaLabel).HasMaxLength(64);

        builder.Property(banner => banner.Placement).HasConversion<string>().HasMaxLength(30).IsRequired();

        // The audience is a flags enum, so it is a small integer rather than a word: 'Everyone' is
        // the two others together, and storing the name would make a query for "banners anonymous
        // visitors see" a string comparison against three different values.
        builder.Property(banner => banner.Audience).HasConversion<int>().IsRequired();

        // The storefront's read: the live banners for one placement, best first. The filter keeps the
        // index to the rows that can possibly answer, which on a store with two years of finished
        // campaigns behind it is a small fraction of the table.
        builder.HasIndex(banner => new { banner.TenantId, banner.Placement, banner.Priority })
            .HasFilter("is_active");
    }
}

/// <summary>Maps <see cref="ProductCollection"/> to <c>content.collections</c>.</summary>
internal sealed class ProductCollectionConfiguration : IEntityTypeConfiguration<ProductCollection>
{
    public void Configure(EntityTypeBuilder<ProductCollection> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("collections", table =>
        {
            table.HasCheckConstraint("ck_collections_type", ContentCheckConstraints.CollectionKinds);
            table.HasCheckConstraint("ck_collections_counts", ContentCheckConstraints.Counts);
        });

        builder.HasKey(collection => collection.Id);
        builder.Property(collection => collection.Id).ValueGeneratedNever();

        builder.Property(collection => collection.Slug)
            .HasMaxLength(ProductCollection.MaxSlugLength)
            .IsRequired();

        builder.Property(collection => collection.Name)
            .HasMaxLength(ProductCollection.MaxNameLength)
            .IsRequired();

        builder.Property(collection => collection.Description)
            .HasMaxLength(ProductCollection.MaxDescriptionLength);

        builder.Property(collection => collection.Kind)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(collection => collection.Rules).HasColumnType(ContentJson.ColumnType).IsRequired();

        builder.OwnsOne(collection => collection.Seo, seo => seo.ToJson());

        builder.HasIndex(collection => new { collection.TenantId, collection.Slug })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        // The refresh sweep: rule-based collections, least recently refreshed first.
        builder.HasIndex(collection => new { collection.Kind, collection.RefreshedAt })
            .HasDatabaseName("ix_collections_stale")
            .HasFilter("type = 'Rule' AND deleted_at IS NULL");
    }
}

/// <summary>Maps <see cref="CollectionItem"/> to <c>content.collection_items</c>.</summary>
internal sealed class CollectionItemConfiguration : IEntityTypeConfiguration<CollectionItem>
{
    public void Configure(EntityTypeBuilder<CollectionItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("collection_items", table =>
            table.HasCheckConstraint("ck_collection_items_position", ContentCheckConstraints.Position));

        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();

        // One row per product per collection. A rule refresh writes the same product twice only if
        // it has been written twice, and a shopper seeing the same card twice in a carousel is the
        // most visible possible symptom of a bug nobody would look for in a rule engine.
        builder.HasIndex(item => new { item.CollectionId, item.ProductId }).IsUnique();

        // The storefront's read: one collection's products, in its own order, pinned rows first.
        builder.HasIndex(item => new { item.CollectionId, item.Position });

        // The event handler's read: every collection a product is in, when the product changes.
        builder.HasIndex(item => new { item.TenantId, item.ProductId });
    }
}

/// <summary>Maps <see cref="Redirect"/> to <c>content.redirects</c>.</summary>
internal sealed class RedirectConfiguration : IEntityTypeConfiguration<Redirect>
{
    public void Configure(EntityTypeBuilder<Redirect> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("redirects", table =>
        {
            table.HasCheckConstraint("ck_redirects_status_code", ContentCheckConstraints.RedirectStatuses);
            table.HasCheckConstraint("ck_redirects_target", ContentCheckConstraints.RedirectTarget);
            table.HasCheckConstraint("ck_redirects_hit_count", ContentCheckConstraints.HitCount);
        });

        builder.HasKey(redirect => redirect.Id);
        builder.Property(redirect => redirect.Id).ValueGeneratedNever();

        builder.Property(redirect => redirect.FromPath).HasMaxLength(Redirect.MaxPathLength).IsRequired();
        builder.Property(redirect => redirect.ToPath).HasMaxLength(Redirect.MaxPathLength);
        builder.Property(redirect => redirect.Note).HasMaxLength(Redirect.MaxNoteLength);

        // Stored as the number it is answered with, so a support engineer reading the table reads
        // "301" rather than a word they then have to map.
        builder.Property(redirect => redirect.Status)
            .HasColumnName("status_code")
            .HasConversion<int>()
            .IsRequired();

        // The lookup the storefront makes on every 404, which is the only reason this table exists.
        builder.HasIndex(redirect => new { redirect.TenantId, redirect.FromPath }).IsUnique();
    }
}
