using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Platform.Domain;
using KlaraHome.Modules.Platform.Infrastructure.Persistence.Configurations;
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
/// From Step 6 it also owns the module's own tables: the tenant, the settings store, the feature
/// flags, the audit trail, and the reference data every other module looks Indian states, PIN codes
/// and HSN chapters up in.
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

    /// <summary>The businesses this deployment serves. One row, in the v1 redistribution model.</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>Store configuration, one row per section.</summary>
    public DbSet<StoreSetting> StoreSettings => Set<StoreSetting>();

    /// <summary>Feature switches an operator can throw without a deploy.</summary>
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();

    /// <summary>The append-only audit trail.</summary>
    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();

    /// <summary>Indian states and union territories, with their GST codes.</summary>
    public DbSet<StateOrUnionTerritory> States => Set<StateOrUnionTerritory>();

    /// <summary>PIN codes and the places they identify.</summary>
    public DbSet<Pincode> Pincodes => Set<Pincode>();

    /// <summary>HSN chapters and codes.</summary>
    public DbSet<HsnCode> HsnCodes => Set<HsnCode>();

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

        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new StoreSettingConfiguration());
        modelBuilder.ApplyConfiguration(new FeatureFlagConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new StateConfiguration());
        modelBuilder.ApplyConfiguration(new PincodeConfiguration());
        modelBuilder.ApplyConfiguration(new HsnCodeConfiguration());
    }
}
