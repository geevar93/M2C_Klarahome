using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Notifications.Domain;
using KlaraHome.Modules.Notifications.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Notifications.Infrastructure.Persistence;

/// <summary>The Notifications module's data access.</summary>
/// <param name="options">Provider options supplied by DI or by the design-time factory.</param>
/// <param name="tenantContext">The ambient tenant.</param>
internal sealed class NotificationsDbContext(
    DbContextOptions<NotificationsDbContext> options,
    ITenantContext tenantContext) : KlaraHomeDbContext(options, tenantContext)
{
    /// <inheritdoc />
    public override string Schema => NotificationsModule.SchemaName;

    /// <summary>What each event says on each channel, in each language.</summary>
    public DbSet<NotificationTemplate> Templates => Set<NotificationTemplate>();

    /// <summary>The delivery log, and the queue the dispatcher drains.</summary>
    public DbSet<NotificationMessage> Messages => Set<NotificationMessage>();

    /// <summary>What each person has chosen to receive.</summary>
    public DbSet<NotificationPreference> Preferences => Set<NotificationPreference>();

    /// <inheritdoc />
    protected override void ConfigureModule(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfiguration(new NotificationTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationMessageConfiguration());
        modelBuilder.ApplyConfiguration(new NotificationPreferenceConfiguration());
    }
}
