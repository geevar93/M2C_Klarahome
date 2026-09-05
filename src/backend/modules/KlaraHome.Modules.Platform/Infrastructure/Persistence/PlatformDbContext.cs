using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Platform.Infrastructure.Persistence;

/// <summary>
/// The Platform module's data access. Internal, like every module context: the host registers it
/// and the migrator resolves it by descriptor, but no other assembly names the type
/// (docs/01-architecture.md §2.1).
/// </summary>
/// <remarks>
/// <para>
/// It carries the migrations for the outbox and inbox because those tables live in the
/// <c>platform</c> schema (docs/03-database-design.md §4.1) and this is the context that owns it.
/// Every other module context maps the same tables for writing but excludes them from its
/// migrations, so the queue is created once and appended to by all.
/// </para>
/// <para>
/// The Platform module's own tables — tenants, settings, feature flags, audit log, reference data
/// — arrive at Step 6. This context exists at Step 4 so the data-access conventions and the
/// migration pipeline are exercised end to end by a real module rather than by a fixture.
/// </para>
/// </remarks>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
internal sealed class PlatformDbContext(DbContextOptions<PlatformDbContext> options, ITenantContext tenantContext)
    : KlaraHomeDbContext(options, tenantContext)
{
    /// <inheritdoc />
    public override string Schema => PlatformModule.SchemaName;

    /// <inheritdoc />
    public override bool OwnsMessagingTables => true;

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Extensions are database-wide, not schema-scoped, so the first module to migrate creates
        // them (docs/03-database-design.md §1). They are declared here rather than left to the
        // step that first needs one, because a CREATE EXTENSION discovered halfway through the
        // Search module is a production database change nobody planned for.
        //   pgcrypto  — digest/gen_random_bytes for token and document hashing
        //   pg_trgm   — trigram similarity for fuzzy product search
        //   unaccent  — accent-insensitive matching
        //   btree_gin — composite GIN indexes mixing scalar and full-text predicates
        // pg_stat_statements is the fifth in that list and is deliberately absent: it needs
        // shared_preload_libraries, which is a server setting, not a migration. It is enabled in
        // the Postgres container configuration at Step 31.
        modelBuilder.HasPostgresExtension("pgcrypto");
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.HasPostgresExtension("unaccent");
        modelBuilder.HasPostgresExtension("btree_gin");

        // Step 6 maps tenants, store_settings, feature_flags, audit_logs and the reference tables
        // here. Nothing else is mapped yet, deliberately: the outbox and inbox come from the base
        // context and are the whole of this schema at Step 4.
    }
}
