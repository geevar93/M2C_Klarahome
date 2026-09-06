using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Modules.Search.Domain;
using KlaraHome.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace KlaraHome.Modules.Search.Infrastructure.Persistence.Configurations;

/// <summary>
/// The <c>CHECK</c> lists and the index expressions, written once so a column and the thing that
/// constrains it cannot drift apart.
/// </summary>
internal static class SearchCheckConstraints
{
    /// <summary>Money on the projection is never negative, and MRP is never below the price.</summary>
    /// <remarks>
    /// The second half is statutory rather than tidy: in India the maximum retail price is a
    /// declaration, and an offer above it is an offence rather than a display bug. The catalogue
    /// refuses one at source; this is the copy saying the same thing.
    /// </remarks>
    public const string Amounts = "mrp >= 0 AND price >= 0 AND mrp >= price";

    /// <summary>A discount is a percentage.</summary>
    public const string Discount = "discount_percent >= 0 AND discount_percent <= 100";

    /// <summary>Counts are counts.</summary>
    public const string Counts =
        "rating_count >= 0 AND offer_count >= 0 AND quantity_available >= 0 AND units_sold >= 0";

    /// <summary>A review score is out of five, or absent.</summary>
    public const string Rating =
        "(rating_average IS NULL OR (rating_average >= 0 AND rating_average <= 5)) "
        + "AND (vendor_rating IS NULL OR (vendor_rating >= 0 AND vendor_rating <= 5))";

    /// <summary>A synonym rule that expands to nothing is a rule that does nothing.</summary>
    /// <remarks>
    /// Enforced here rather than left to the handler because an empty expansion list is invisible on
    /// the admin screen: the rule looks configured, it is listed, and it silently never fires.
    /// </remarks>
    public const string Expansions = "cardinality(expansions) > 0";

    /// <summary>
    /// The <c>tsvector</c> the free-text index is built on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Four weights, and the order is the order a shopper means them in. <b>A</b> is the product's
    /// own name, which is what they are almost always typing. <b>B</b> is the brand and the variant,
    /// because "Klara cushion" and a SKU pasted from an invoice must both land. <b>C</b> is the
    /// category, so "lighting" finds lamps that never say the word. <b>D</b> is everything else — the
    /// summary and the searchable attributes — which should be findable without ever outranking a
    /// product actually named for the word.
    /// </para>
    /// <para>
    /// Generated and stored, not a trigger. PostgreSQL keeps it in step with the columns it is built
    /// from, so there is no path by which a row is written and its search text is not — including the
    /// bulk rebuild, which is the path a trigger is most likely to be written to bypass.
    /// </para>
    /// <para>
    /// <c>to_tsvector(regconfig, text)</c> with a literal configuration is immutable, which is what
    /// makes it legal in a generated column at all. The single-argument form reads
    /// <c>default_text_search_config</c> and is not, so it cannot be used here even though it looks
    /// tidier.
    /// </para>
    /// </remarks>
    public const string SearchVectorSql = """
        setweight(to_tsvector('english', coalesce(product_name, '')), 'A') ||
        setweight(to_tsvector('english', coalesce(brand_name, '') || ' ' || coalesce(variant_name, '') || ' ' || coalesce(sku, '')), 'B') ||
        setweight(to_tsvector('english', coalesce(category_name, '')), 'C') ||
        setweight(to_tsvector('english', coalesce(keywords, '')), 'D')
        """;
}

/// <summary>Maps <see cref="ProductSearchDocument"/> to <c>search.product_search_projection</c>.</summary>
internal sealed class ProductSearchDocumentConfiguration : IEntityTypeConfiguration<ProductSearchDocument>
{
    public void Configure(EntityTypeBuilder<ProductSearchDocument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("product_search_projection", table =>
        {
            table.HasCheckConstraint("ck_product_search_projection_amounts", SearchCheckConstraints.Amounts);
            table.HasCheckConstraint("ck_product_search_projection_discount", SearchCheckConstraints.Discount);
            table.HasCheckConstraint("ck_product_search_projection_counts", SearchCheckConstraints.Counts);
            table.HasCheckConstraint("ck_product_search_projection_rating", SearchCheckConstraints.Rating);
        });

        builder.HasKey(document => document.Id);
        builder.Property(document => document.Id).ValueGeneratedNever();

        builder.Property(document => document.Sku).HasMaxLength(ProductSearchDocument.MaxNameLength).IsRequired();
        builder.Property(document => document.ProductName).HasMaxLength(ProductSearchDocument.MaxNameLength).IsRequired();
        builder.Property(document => document.VariantName).HasMaxLength(ProductSearchDocument.MaxNameLength).IsRequired();
        builder.Property(document => document.ProductSlug).HasMaxLength(220).IsRequired();
        builder.Property(document => document.BrandName).HasMaxLength(ProductSearchDocument.MaxNameLength);
        builder.Property(document => document.BrandSlug).HasMaxLength(220);
        builder.Property(document => document.CategoryName).HasMaxLength(ProductSearchDocument.MaxNameLength).IsRequired();
        builder.Property(document => document.CategorySlug).HasMaxLength(220).IsRequired();
        builder.Property(document => document.CategoryPath).HasMaxLength(1024).IsRequired();
        builder.Property(document => document.VendorName).HasMaxLength(ProductSearchDocument.MaxNameLength).IsRequired();
        builder.Property(document => document.VendorSlug).HasMaxLength(220).IsRequired();
        builder.Property(document => document.Keywords).HasMaxLength(ProductSearchDocument.MaxKeywordsLength);

        builder.Property(document => document.CategoryIds).HasColumnType("uuid[]").IsRequired();

        builder.Property(document => document.Attributes).HasColumnType("jsonb").IsRequired();
        builder.Property(document => document.AttributeMeta).HasColumnType("jsonb").IsRequired();

        builder.Property(document => document.Mrp).HasColumnType(ModelConventions.MoneyColumnType);
        builder.Property(document => document.Price).HasColumnType(ModelConventions.MoneyColumnType);

        builder.Property(document => document.CurrencyCode)
            .HasColumnType(ModelConventions.CurrencyColumnType)
            .HasDefaultValue(Money.Inr);

        builder.Property(document => document.RatingAverage).HasColumnType("numeric(3,2)");
        builder.Property(document => document.VendorRating).HasColumnType("numeric(3,2)");
        builder.Property(document => document.PopularityScore).HasColumnType("numeric(12,4)");

        // One row per variant, and the unique index is what enforces it. Every write in this module
        // is an upsert keyed on this, so a redelivered integration event and a concurrent rebuild
        // cannot between them produce two rows for one sellable thing.
        builder.HasIndex(document => new { document.TenantId, document.VariantId }).IsUnique();

        // The browse query: a category and everything beneath it, in price order.
        // docs/03-database-design.md §5 names this one.
        builder.HasIndex(document => new { document.TenantId, document.CategoryPath, document.Price });

        // The brand and seller landing pages, and the two most-used facet filters.
        builder.HasIndex(document => new { document.TenantId, document.BrandId });
        builder.HasIndex(document => new { document.TenantId, document.VendorId });

        // The default order of a browse page with no query at all: the things people buy.
        builder.HasIndex(document => new { document.TenantId, document.IsActive, document.PopularityScore });

        // What the reindex sweep looks for. Filtered, because the rows that have fallen behind are
        // a small and usually empty slice of a table that has one row per sellable thing.
        builder
            .HasIndex(document => new { document.TenantId, document.IndexedAt })
            .HasDatabaseName("ix_product_search_projection_stale")
            .HasFilter("is_active = true");

        // The translation an integration event needs: it names a listing, this table is keyed on a
        // variant, and the row that has to be repriced is the one whose buy box that listing won.
        builder.HasIndex(document => new { document.TenantId, document.ListingId });

        // Declared as a shadow property, exactly as the catalogue's own does. The CLR type belongs
        // to Npgsql, and a Domain entity that named it would break the "the Domain layer knows
        // nothing about persistence" boundary the architecture tests enforce. Nothing reads it in
        // C#: every query that touches it is raw SQL.
        builder.Property<NpgsqlTsVector>("SearchVector")
            .HasColumnName("search_vector")
            .HasColumnType("tsvector")
            .HasComputedColumnSql(SearchCheckConstraints.SearchVectorSql, stored: true);

        builder.HasIndex("SearchVector")
            .HasDatabaseName("ix_product_search_projection_search_vector")
            .HasMethod("gin");

        // The filter index for every attribute facet at once. One GIN index over the whole document
        // answers "has colour beige" and "has size m" without a column per attribute — which is the
        // entire reason the attributes are jsonb rather than a table nobody could index usefully.
        builder.HasIndex(document => document.Attributes)
            .HasDatabaseName("ix_product_search_projection_attributes")
            .HasMethod("gin");

        // "Everything under Furniture", from the id the shopper clicked and nothing else. A prefix
        // match on the path would need the caller to know the ancestors and a substring match could
        // not use an index at all; array containment against a GIN index needs neither.
        builder.HasIndex(document => document.CategoryIds)
            .HasDatabaseName("ix_product_search_projection_category_ids")
            .HasMethod("gin");

        // The fuzzy pass. Trigram rather than full text, because a misspelling produces no lexeme
        // the exact index could match: "cushin" and "cushion" share no stem and eight trigrams.
        builder.HasIndex(document => document.ProductName)
            .HasDatabaseName("ix_product_search_projection_product_name_trgm")
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");

        builder.Ignore(document => document.DomainEvents);
    }
}

/// <summary>Maps <see cref="SearchSynonym"/> to <c>search.search_synonyms</c>.</summary>
internal sealed class SearchSynonymConfiguration : IEntityTypeConfiguration<SearchSynonym>
{
    public void Configure(EntityTypeBuilder<SearchSynonym> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("search_synonyms", table =>
            table.HasCheckConstraint("ck_search_synonyms_expansions", SearchCheckConstraints.Expansions));

        builder.HasKey(synonym => synonym.Id);
        builder.Property(synonym => synonym.Id).ValueGeneratedNever();

        builder.Property(synonym => synonym.Term).HasMaxLength(SearchSynonym.MaxTermLength).IsRequired();
        builder.Property(synonym => synonym.Note).HasMaxLength(500);

        // A native text[] rather than a delimited string. The values are a set, the database can say
        // so, and a comma-separated column is a parsing decision every reader has to repeat and one
        // of them eventually gets wrong.
        builder.Property(synonym => synonym.Expansions)
            .HasColumnType("text[]")
            .IsRequired();

        // One rule per word. Two rows for "sofa" would be two answers to one question, and which one
        // applied would depend on the order the loader happened to read them.
        builder.HasIndex(synonym => new { synonym.TenantId, synonym.Term }).IsUnique();

        builder.Ignore(synonym => synonym.DomainEvents);
    }
}

/// <summary>Maps <see cref="SearchStopWord"/> to <c>search.search_stop_words</c>.</summary>
internal sealed class SearchStopWordConfiguration : IEntityTypeConfiguration<SearchStopWord>
{
    public void Configure(EntityTypeBuilder<SearchStopWord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("search_stop_words");

        builder.HasKey(word => word.Id);
        builder.Property(word => word.Id).ValueGeneratedNever();

        builder.Property(word => word.Word).HasMaxLength(SearchStopWord.MaxWordLength).IsRequired();

        builder.HasIndex(word => new { word.TenantId, word.Word }).IsUnique();

        builder.Ignore(word => word.DomainEvents);
    }
}

/// <summary>
/// Maps <see cref="SearchQueryLogEntry"/> to <c>search.search_queries</c>.
/// </summary>
/// <remarks>
/// Excluded from migrations: the table is <c>PARTITION BY RANGE (created_at)</c> and is created by
/// hand in a migration of its own, for the reason every partitioned table in this solution is — a
/// partitioned table is created partitioned or not at all, and EF does not generate the clause.
/// </remarks>
internal sealed class SearchQueryLogEntryConfiguration : IEntityTypeConfiguration<SearchQueryLogEntry>
{
    public void Configure(EntityTypeBuilder<SearchQueryLogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("search_queries", table => table.ExcludeFromMigrations());

        // Composite, and in this order: PostgreSQL requires the partition key to be part of every
        // unique constraint on a partitioned table.
        builder.HasKey(entry => new { entry.CreatedAt, entry.Id });

        builder.Property(entry => entry.Id).ValueGeneratedNever();

        builder.Property(entry => entry.QueryText).HasMaxLength(SearchQueryLogEntry.MaxQueryLength).IsRequired();
        builder.Property(entry => entry.NormalisedQuery).HasMaxLength(SearchQueryLogEntry.MaxQueryLength).IsRequired();
        builder.Property(entry => entry.Source).HasMaxLength(16).IsRequired();
        builder.Property(entry => entry.SessionId).HasMaxLength(64);
        builder.Property(entry => entry.Filters).HasColumnType("jsonb");

        builder.Ignore(entry => entry.DomainEvents);
    }
}
