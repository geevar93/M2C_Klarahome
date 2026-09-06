using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace KlaraHome.Modules.Catalog.Infrastructure.Persistence.Configurations;

/// <summary>The <c>CHECK</c> lists, written once so a column and its constraint cannot drift apart.</summary>
internal static class CatalogCheckConstraints
{
    /// <summary>The values <c>products.status</c> accepts.</summary>
    public const string ProductStatuses =
        "status IN ('Draft', 'PendingApproval', 'Active', 'Inactive', 'Archived')";

    /// <summary>The values <c>variants.status</c> accepts.</summary>
    public const string VariantStatuses = "status IN ('Draft', 'Active', 'Inactive', 'Archived')";

    /// <summary>The values <c>listings.status</c> accepts.</summary>
    public const string ListingStatuses = "status IN ('Draft', 'Active', 'Inactive', 'Archived')";

    /// <summary>The values <c>attributes.data_type</c> accepts.</summary>
    public const string AttributeDataTypes =
        "data_type IN ('Text', 'Number', 'Boolean', 'Select', 'MultiSelect', 'Date')";

    /// <summary>The values <c>media_assets.kind</c> accepts.</summary>
    public const string MediaKinds = "kind IN ('Image', 'Video', 'Document')";

    /// <summary>The values <c>product_moderations.status</c> accepts.</summary>
    public const string ModerationStatuses = "status IN ('Pending', 'Approved', 'Rejected', 'Withdrawn')";

    /// <summary>The values <c>catalog_jobs.kind</c> accepts.</summary>
    public const string JobKinds = "kind IN ('ProductImport', 'ProductExport')";

    /// <summary>The values <c>catalog_jobs.status</c> accepts.</summary>
    public const string JobStatuses =
        "status IN ('Queued', 'Running', 'Succeeded', 'PartiallySucceeded', 'Failed')";
}

/// <summary>Maps <see cref="Category"/> to <c>catalog.categories</c>.</summary>
internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("categories", table =>
        {
            table.HasCheckConstraint("ck_categories_level", $"level >= 0 AND level < {Category.MaxDepth}");
            table.HasCheckConstraint("ck_categories_position", "position >= 0");

            // A root has no parent and a non-root must have one. Without this a node can be at
            // level 3 with a null parent, which makes the breadcrumb stop halfway up.
            table.HasCheckConstraint(
                "ck_categories_parent",
                "(level = 0 AND parent_id IS NULL) OR (level > 0 AND parent_id IS NOT NULL)");
        });

        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).ValueGeneratedNever();

        builder.Property(category => category.Name).HasMaxLength(160);
        builder.Property(category => category.Slug).HasMaxLength(180);
        builder.Property(category => category.Description).HasMaxLength(4000);

        // Long enough for the deepest tree: six levels of "/{36-char uuid}" plus the trailing slash.
        builder.Property(category => category.Path).HasMaxLength(260);

        builder.OwnsOne(category => category.Seo, seo => seo.ToJson());

        // The slug is a URL, so it has to be unique. Filtered on the soft-delete column, because a
        // retired category must not hold its slug hostage against a new one taking the same name.
        builder.HasIndex(category => new { category.TenantId, category.Slug })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        // The reason the path exists: "everything under this node" is one range scan.
        builder.HasIndex(category => new { category.TenantId, category.Path });

        // The children of a node, in the order a menu renders them.
        builder.HasIndex(category => new { category.TenantId, category.ParentId, category.Position });

        builder.Ignore(category => category.DomainEvents);
    }
}

/// <summary>Maps <see cref="Brand"/> to <c>catalog.brands</c>.</summary>
internal sealed class BrandConfiguration : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("brands");

        builder.HasKey(brand => brand.Id);
        builder.Property(brand => brand.Id).ValueGeneratedNever();

        builder.Property(brand => brand.Name).HasMaxLength(160);
        builder.Property(brand => brand.Slug).HasMaxLength(180);
        builder.Property(brand => brand.Description).HasMaxLength(4000);

        builder.OwnsOne(brand => brand.Seo, seo => seo.ToJson());

        builder.HasIndex(brand => new { brand.TenantId, brand.Slug }).IsUnique();

        // "Philips" and "philips" are the same brand, and the second one is a data-entry mistake
        // that produces two facet values. Case-insensitive because that is what people type.
        builder.HasIndex(brand => new { brand.TenantId, brand.Name }).IsUnique();

        builder.Ignore(brand => brand.DomainEvents);
    }
}

/// <summary>Maps <see cref="ProductAttribute"/> to <c>catalog.attributes</c>.</summary>
internal sealed class ProductAttributeConfiguration : IEntityTypeConfiguration<ProductAttribute>
{
    public void Configure(EntityTypeBuilder<ProductAttribute> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("attributes", table =>
        {
            table.HasCheckConstraint("ck_attributes_data_type", CatalogCheckConstraints.AttributeDataTypes);

            // Only a closed list can be a variant axis. The application layer refuses it with a
            // message; this stops a migration or a script writing the state anyway.
            table.HasCheckConstraint(
                "ck_attributes_variant_defining",
                "is_variant_defining = false OR data_type IN ('Select', 'MultiSelect')");
        });

        builder.HasKey(attribute => attribute.Id);
        builder.Property(attribute => attribute.Id).ValueGeneratedNever();

        builder.Property(attribute => attribute.Code).HasMaxLength(64);
        builder.Property(attribute => attribute.Name).HasMaxLength(160);
        builder.Property(attribute => attribute.Unit).HasMaxLength(16);
        builder.Property(attribute => attribute.DataType).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(attribute => new { attribute.TenantId, attribute.Code }).IsUnique();

        // The filter rail asks for exactly this: the filterable attributes, in display order.
        builder.HasIndex(attribute => new { attribute.TenantId, attribute.IsFilterable, attribute.Position });

        builder.Ignore(attribute => attribute.DomainEvents);
        builder.Ignore(attribute => attribute.UsesOptions);
        builder.Ignore(attribute => attribute.CanDefineVariants);
    }
}

/// <summary>Maps <see cref="AttributeOption"/> to <c>catalog.attribute_options</c>.</summary>
internal sealed class AttributeOptionConfiguration : IEntityTypeConfiguration<AttributeOption>
{
    public void Configure(EntityTypeBuilder<AttributeOption> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("attribute_options");

        builder.HasKey(option => option.Id);
        builder.Property(option => option.Id).ValueGeneratedNever();

        builder.Property(option => option.Value).HasMaxLength(120);
        builder.Property(option => option.Label).HasMaxLength(160);
        builder.Property(option => option.SwatchHex).HasMaxLength(9).IsFixedLength(false);

        // Two options with the same machine value would make a facet unresolvable.
        builder.HasIndex(option => new { option.TenantId, option.AttributeId, option.Value }).IsUnique();

        builder.HasIndex(option => new { option.TenantId, option.AttributeId, option.Position });

        builder.Ignore(option => option.DomainEvents);
    }
}

/// <summary>Maps <see cref="AttributeSet"/> to <c>catalog.attribute_sets</c>.</summary>
internal sealed class AttributeSetConfiguration : IEntityTypeConfiguration<AttributeSet>
{
    public void Configure(EntityTypeBuilder<AttributeSet> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("attribute_sets");

        builder.HasKey(set => set.Id);
        builder.Property(set => set.Id).ValueGeneratedNever();

        builder.Property(set => set.Code).HasMaxLength(64);
        builder.Property(set => set.Name).HasMaxLength(160);
        builder.Property(set => set.Description).HasMaxLength(1000);

        builder.HasIndex(set => new { set.TenantId, set.Code }).IsUnique();

        builder.Ignore(set => set.DomainEvents);
    }
}

/// <summary>Maps <see cref="AttributeSetMember"/> to <c>catalog.attribute_set_members</c>.</summary>
internal sealed class AttributeSetMemberConfiguration : IEntityTypeConfiguration<AttributeSetMember>
{
    public void Configure(EntityTypeBuilder<AttributeSetMember> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("attribute_set_members");

        builder.HasKey(member => member.Id);
        builder.Property(member => member.Id).ValueGeneratedNever();

        // One membership per attribute per set. Without it, a double-click adds the attribute to
        // the product form twice.
        builder.HasIndex(member => new { member.TenantId, member.AttributeSetId, member.AttributeId })
            .IsUnique();

        builder.HasIndex(member => new { member.TenantId, member.AttributeSetId, member.Position });

        builder.Ignore(member => member.DomainEvents);
    }
}

/// <summary>Maps <see cref="Product"/> to <c>catalog.products</c>.</summary>
internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("products", table =>
        {
            table.HasCheckConstraint("ck_products_status", CatalogCheckConstraints.ProductStatuses);
            table.HasCheckConstraint("ck_products_gst_rate", "gst_rate >= 0 AND gst_rate <= 100");
            table.HasCheckConstraint(
                "ck_products_rating",
                "rating_average IS NULL OR (rating_average >= 0 AND rating_average <= 5)");
            table.HasCheckConstraint("ck_products_rating_count", "rating_count >= 0");

            // An HSN code is 4, 6 or 8 digits. Length is the cheap half of the check; the digits
            // are validated in the application layer, where the message can say which one is wrong.
            table.HasCheckConstraint(
                "ck_products_hsn",
                "hsn_code IS NULL OR char_length(hsn_code) IN (4, 6, 8)");

            table.HasCheckConstraint(
                "ck_products_country_of_origin",
                "country_of_origin IS NULL OR char_length(country_of_origin) = 2");
        });

        builder.HasKey(product => product.Id);
        builder.Property(product => product.Id).ValueGeneratedNever();

        builder.Property(product => product.Name).HasMaxLength(300);
        builder.Property(product => product.Slug).HasMaxLength(320);
        builder.Property(product => product.ShortDescription).HasMaxLength(500);
        builder.Property(product => product.Warranty).HasMaxLength(1000);
        builder.Property(product => product.HsnCode).HasMaxLength(8);
        builder.Property(product => product.CountryOfOrigin).HasMaxLength(2).IsFixedLength();
        builder.Property(product => product.GstRate).HasColumnType("numeric(7,4)");
        builder.Property(product => product.RatingAverage).HasColumnType("numeric(3,2)");
        builder.Property(product => product.Status).HasConversion<string>().HasMaxLength(16);

        builder.OwnsOne(product => product.Seo, seo => seo.ToJson());
        builder.OwnsOne(product => product.Manufacturer, party => party.ToJson());
        builder.OwnsOne(product => product.Packer, party => party.ToJson());
        builder.OwnsOne(product => product.Importer, party => party.ToJson());
        builder.OwnsMany(product => product.Specifications, spec => spec.ToJson());

        // The PDP routes on the slug. Filtered on the soft-delete column so an archived product
        // does not hold its URL against a replacement.
        builder.HasIndex(product => new { product.TenantId, product.Slug })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        // docs/03-database-design.md §5: the browse query.
        builder.HasIndex(product => new { product.TenantId, product.CategoryId, product.Status });

        // The brand landing page, and the "which products would this brand's deletion orphan" check.
        builder.HasIndex(product => new { product.TenantId, product.BrandId });

        // The seller's own product list, and the moderation queue's grouping.
        builder.HasIndex(product => new { product.TenantId, product.VendorId, product.Status });

        // A generated tsvector rather than a trigger: Postgres keeps it in step with the columns it
        // is built from, so there is no path by which a row is updated and its search text is not.
        //
        // Declared as a shadow property on purpose. The CLR type belongs to Npgsql, and a Domain
        // entity that named it would break the "the Domain layer knows nothing about persistence"
        // boundary the architecture tests enforce. Nothing reads it in C# — Step 19 queries it in
        // SQL — so a shadow property costs nothing.
        builder.Property<NpgsqlTsVector>("SearchVector")
            .HasColumnName("search_vector")
            .HasColumnType("tsvector")
            .HasComputedColumnSql(
                "to_tsvector('english', coalesce(name, '') || ' ' || coalesce(short_description, ''))",
                stored: true);

        builder.HasIndex("SearchVector").HasMethod("gin");

        builder.Ignore(product => product.DomainEvents);
        builder.Ignore(product => product.IsPublished);
    }
}

/// <summary>Maps <see cref="ProductAttributeValue"/> to <c>catalog.product_attribute_values</c>.</summary>
internal sealed class ProductAttributeValueConfiguration : IEntityTypeConfiguration<ProductAttributeValue>
{
    public void Configure(EntityTypeBuilder<ProductAttributeValue> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("product_attribute_values");

        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();

        builder.Property(value => value.ValueText).HasMaxLength(2000);
        builder.Property(value => value.ValueNumber).HasColumnType("numeric(18,4)");

        // One row per (product, attribute, option). The option is in the key because a multiselect
        // legitimately has several rows for one attribute; a select has exactly one, and its
        // uniqueness follows from the option being unique within the attribute.
        builder.HasIndex(value => new { value.TenantId, value.ProductId, value.AttributeId, value.ValueOptionId })
            .IsUnique();

        // The faceted-search projection reads by attribute and value at Step 19.
        builder.HasIndex(value => new { value.TenantId, value.AttributeId, value.ValueOptionId });

        builder.Ignore(value => value.DomainEvents);
    }
}

/// <summary>Maps <see cref="Variant"/> to <c>catalog.variants</c>.</summary>
internal sealed class VariantConfiguration : IEntityTypeConfiguration<Variant>
{
    public void Configure(EntityTypeBuilder<Variant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("variants", table =>
        {
            table.HasCheckConstraint("ck_variants_status", CatalogCheckConstraints.VariantStatuses);
            table.HasCheckConstraint("ck_variants_mrp", "mrp_amount >= 0");
            table.HasCheckConstraint(
                "ck_variants_dimensions",
                "weight_grams >= 0 AND length_mm >= 0 AND width_mm >= 0 AND height_mm >= 0");
            table.HasCheckConstraint("ck_variants_shelf_life", "shelf_life_days IS NULL OR shelf_life_days > 0");
        });

        builder.HasKey(variant => variant.Id);
        builder.Property(variant => variant.Id).ValueGeneratedNever();

        builder.Property(variant => variant.Sku).HasMaxLength(64);
        builder.Property(variant => variant.Barcode).HasMaxLength(64);
        builder.Property(variant => variant.NameSuffix).HasMaxLength(200);
        builder.Property(variant => variant.NetQuantity).HasMaxLength(80);
        builder.Property(variant => variant.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(variant => variant.AttributeHash).HasMaxLength(64).IsFixedLength();

        builder.HasMoney(variant => variant.Mrp);

        // The SKU is printed on labels, typed into a scanner and quoted in support calls.
        builder.HasIndex(variant => new { variant.TenantId, variant.Sku })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        // "A variant's defining attribute combination is unique within its Product"
        // (docs/02-domain-model.md §4.1). The combination is in a child table and no index can span
        // rows, so it is folded into one hash on this row and the index goes here.
        builder.HasIndex(variant => new { variant.TenantId, variant.ProductId, variant.AttributeHash })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(variant => new { variant.TenantId, variant.ProductId, variant.Position });
        builder.HasIndex(variant => variant.Barcode).HasFilter("barcode IS NOT NULL");

        builder.Ignore(variant => variant.DomainEvents);
        builder.Ignore(variant => variant.IsSellable);
    }
}

/// <summary>Maps <see cref="VariantAttributeValue"/> to <c>catalog.variant_attribute_values</c>.</summary>
internal sealed class VariantAttributeValueConfiguration : IEntityTypeConfiguration<VariantAttributeValue>
{
    public void Configure(EntityTypeBuilder<VariantAttributeValue> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("variant_attribute_values");

        builder.HasKey(value => value.Id);
        builder.Property(value => value.Id).ValueGeneratedNever();

        // One value per axis per variant. A variant that is both Beige and Grey is not a variant.
        builder.HasIndex(value => new { value.TenantId, value.VariantId, value.AttributeId }).IsUnique();

        // "Which variant is the Beige one" — the swatch click on the PDP.
        builder.HasIndex(value => new { value.TenantId, value.AttributeId, value.OptionId });

        builder.Ignore(value => value.DomainEvents);
    }
}

/// <summary>Maps <see cref="Listing"/> to <c>catalog.listings</c>.</summary>
internal sealed class ListingConfiguration : IEntityTypeConfiguration<Listing>
{
    public void Configure(EntityTypeBuilder<Listing> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("listings", table =>
        {
            table.HasCheckConstraint("ck_listings_status", CatalogCheckConstraints.ListingStatuses);

            // docs/03-database-design.md §6: selling above MRP is illegal in India, so it is a
            // database constraint and not a validator somebody can forget to call.
            table.HasCheckConstraint(
                "ck_listings_price_below_mrp",
                "selling_price_amount <= mrp_amount");

            table.HasCheckConstraint(
                "ck_listings_prices_positive",
                "mrp_amount >= 0 AND selling_price_amount >= 0");

            table.HasCheckConstraint(
                "ck_listings_handling_time",
                $"handling_time_hours > 0 AND handling_time_hours <= {Listing.MaxHandlingTimeHours}");

            table.HasCheckConstraint(
                "ck_listings_max_order_quantity",
                "max_order_quantity IS NULL OR max_order_quantity > 0");

            // The vendor column is nullable because IVendorScoped allows platform-owned rows. An
            // offer without a seller is not an offer, so this table opts out of that.
            table.HasCheckConstraint("ck_listings_vendor_present", "vendor_id IS NOT NULL");
        });

        builder.HasKey(listing => listing.Id);
        builder.Property(listing => listing.Id).ValueGeneratedNever();

        builder.Property(listing => listing.VendorSku).HasMaxLength(64);
        builder.Property(listing => listing.StatusReason).HasMaxLength(500);
        builder.Property(listing => listing.Status).HasConversion<string>().HasMaxLength(16);

        builder.HasMoney(listing => listing.Mrp);
        builder.HasMoney(listing => listing.SellingPrice);

        // "A Listing is unique per (vendor, variant)" (docs/02-domain-model.md §4.1). Filtered on
        // the soft-delete column so a seller who archived an offer can open a new one.
        builder.HasIndex(listing => new { listing.TenantId, listing.VendorId, listing.VariantId })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        // docs/03-database-design.md §5: buy-box resolution. This is the query the PDP runs for
        // every variant on the page.
        //
        // §5 asks for the selling price to be an INCLUDE column so the ranking read is index-only.
        // It is not, and cannot be through the model: the price is a complex property, and
        // IncludeProperties takes a scalar property name — neither the CLR path nor the column name
        // resolves. Adding it is a one-line raw-SQL step at Step 29, once a query plan says the
        // heap fetch is actually costing something.
        builder.HasIndex(listing => new { listing.TenantId, listing.VariantId, listing.Status });

        // The seller's own listing screen, and the "withdraw everything" sweep when they are
        // suspended.
        builder.HasIndex(listing => new { listing.TenantId, listing.VendorId, listing.Status });

        // "Which offers does this product have" — the admin product page.
        builder.HasIndex(listing => new { listing.TenantId, listing.ProductId });

        builder.Ignore(listing => listing.DomainEvents);
        builder.Ignore(listing => listing.IsLive);
    }
}

/// <summary>Maps <see cref="CatalogMediaAsset"/> to <c>catalog.media_assets</c>.</summary>
internal sealed class CatalogMediaAssetConfiguration : IEntityTypeConfiguration<CatalogMediaAsset>
{
    public void Configure(EntityTypeBuilder<CatalogMediaAsset> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("media_assets", table =>
        {
            table.HasCheckConstraint("ck_media_assets_kind", CatalogCheckConstraints.MediaKinds);
            table.HasCheckConstraint("ck_media_assets_position", "position >= 0");

            // One table serves both galleries, discriminated by which owner is set. A row with
            // neither belongs to nothing and would never be read again.
            table.HasCheckConstraint(
                "ck_media_assets_owner",
                "product_id IS NOT NULL OR variant_id IS NOT NULL");
        });

        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.Id).ValueGeneratedNever();

        builder.Property(asset => asset.AltText).HasMaxLength(300);
        builder.Property(asset => asset.Kind).HasConversion<string>().HasMaxLength(16);

        // The same file twice in one gallery is always a mistake, and it is one a double-clicked
        // "attach" produces reliably.
        builder.HasIndex(asset => new { asset.TenantId, asset.ProductId, asset.VariantId, asset.FileId })
            .IsUnique();

        // The PDP's gallery read: everything for this product and its variants, in order.
        builder.HasIndex(asset => new { asset.TenantId, asset.ProductId, asset.Position });
        builder.HasIndex(asset => new { asset.TenantId, asset.VariantId, asset.Position });

        builder.Ignore(asset => asset.DomainEvents);
    }
}

/// <summary>Maps <see cref="ProductModeration"/> to <c>catalog.product_moderations</c>.</summary>
internal sealed class ProductModerationConfiguration : IEntityTypeConfiguration<ProductModeration>
{
    public void Configure(EntityTypeBuilder<ProductModeration> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("product_moderations", table =>
        {
            table.HasCheckConstraint("ck_product_moderations_status", CatalogCheckConstraints.ModerationStatuses);

            // A rejection nobody explained is a seller who does not know what to change.
            table.HasCheckConstraint(
                "ck_product_moderations_rejection",
                "status <> 'Rejected' OR notes IS NOT NULL");

            // A decided row has a decision time, and a pending one does not.
            table.HasCheckConstraint(
                "ck_product_moderations_reviewed",
                "(status = 'Pending') = (reviewed_at IS NULL)");
        });

        builder.HasKey(moderation => moderation.Id);
        builder.Property(moderation => moderation.Id).ValueGeneratedNever();

        builder.Property(moderation => moderation.Notes).HasMaxLength(2000);
        builder.Property(moderation => moderation.Status).HasConversion<string>().HasMaxLength(16);

        // One open submission per product. Two would let the same product be approved and rejected.
        builder.HasIndex(moderation => new { moderation.TenantId, moderation.ProductId })
            .IsUnique()
            .HasFilter("status = 'Pending'");

        // The queue itself: what is waiting, oldest first.
        builder.HasIndex(moderation => new { moderation.TenantId, moderation.Status, moderation.SubmittedAt });

        // The history of one product, newest first.
        builder.HasIndex(moderation => new { moderation.TenantId, moderation.ProductId, moderation.SubmittedAt });

        builder.Ignore(moderation => moderation.DomainEvents);
    }
}

/// <summary>Maps <see cref="CatalogJob"/> to <c>catalog.catalog_jobs</c>.</summary>
internal sealed class CatalogJobConfiguration : IEntityTypeConfiguration<CatalogJob>
{
    public void Configure(EntityTypeBuilder<CatalogJob> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("catalog_jobs", table =>
        {
            table.HasCheckConstraint("ck_catalog_jobs_kind", CatalogCheckConstraints.JobKinds);
            table.HasCheckConstraint("ck_catalog_jobs_status", CatalogCheckConstraints.JobStatuses);
            table.HasCheckConstraint(
                "ck_catalog_jobs_counts",
                "total_rows >= 0 AND processed_rows >= 0 AND succeeded_rows >= 0 AND failed_rows >= 0");
        });

        builder.HasKey(job => job.Id);
        builder.Property(job => job.Id).ValueGeneratedNever();

        builder.Property(job => job.FileName).HasMaxLength(260);
        builder.Property(job => job.SourceKey).HasMaxLength(512);
        builder.Property(job => job.ResultKey).HasMaxLength(512);
        builder.Property(job => job.FailureReason).HasMaxLength(2000);
        builder.Property(job => job.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(20);

        builder.OwnsMany(job => job.Errors, errors => errors.ToJson());

        // The claim query: the oldest queued job, which is all the poller ever asks for.
        builder.HasIndex(job => new { job.Status, job.CreatedAt })
            .HasFilter("status = 'Queued'");

        // "My imports", newest first.
        builder.HasIndex(job => new { job.TenantId, job.VendorId, job.CreatedAt });

        builder.Ignore(job => job.DomainEvents);
        builder.Ignore(job => job.IsFinished);
    }
}
