using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Media.Domain;
using KlaraHome.Modules.Media.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Media.Infrastructure.Persistence;

/// <summary>
/// The Media module's data access: one table, and the outbox every module maps.
/// </summary>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
internal sealed class MediaDbContext(DbContextOptions<MediaDbContext> options, ITenantContext tenantContext)
    : KlaraHomeDbContext(options, tenantContext)
{
    /// <inheritdoc />
    public override string Schema => MediaModule.SchemaName;

    /// <summary>Every file this deployment has stored.</summary>
    public DbSet<StoredFile> Files => Set<StoredFile>();

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new StoredFileConfiguration());
    }
}
