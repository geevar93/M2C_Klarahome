using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Search.Domain;
using KlaraHome.Modules.Search.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Search.Infrastructure.Persistence;

/// <summary>
/// The Search module's data access.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is vendor-scoped, and that is deliberate rather than an omission. The projection is
/// what a shopper searches, so it must contain every seller's offers at once; a vendor filter on it
/// would mean a seller signing in to the admin app and finding that the storefront's search index
/// contains only their own products. A seller's view of their own listings is the catalogue's
/// question, and the catalogue's tables are scoped for it.
/// </para>
/// <para>
/// Three of the four tables are a copy of facts other modules own and can be rebuilt from them. The
/// fourth, the query log, is the only original record in this schema: what a shopper typed exists
/// nowhere else, which is why it is partitioned and retained rather than treated as a cache.
/// </para>
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
/// <param name="callerContext">The current caller, read by the vendor query filter.</param>
internal sealed class SearchDbContext(
    DbContextOptions<SearchDbContext> options,
    ITenantContext tenantContext,
    ICallerContext? callerContext = null)
    : KlaraHomeDbContext(options, tenantContext, callerContext)
{
    /// <inheritdoc />
    public override string Schema => SearchModule.SchemaName;

    /// <summary>The index: one row per variant, carrying the offer that won its buy box.</summary>
    public DbSet<ProductSearchDocument> Documents => Set<ProductSearchDocument>();

    /// <summary>The words this store's shoppers use, and the words the catalogue uses instead.</summary>
    public DbSet<SearchSynonym> Synonyms => Set<SearchSynonym>();

    /// <summary>The words this store wants ignored in a query.</summary>
    public DbSet<SearchStopWord> StopWords => Set<SearchStopWord>();

    /// <summary>What shoppers asked for, and what came back.</summary>
    public DbSet<SearchQueryLogEntry> Queries => Set<SearchQueryLogEntry>();

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new ProductSearchDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new SearchSynonymConfiguration());
        modelBuilder.ApplyConfiguration(new SearchStopWordConfiguration());
        modelBuilder.ApplyConfiguration(new SearchQueryLogEntryConfiguration());
    }
}
