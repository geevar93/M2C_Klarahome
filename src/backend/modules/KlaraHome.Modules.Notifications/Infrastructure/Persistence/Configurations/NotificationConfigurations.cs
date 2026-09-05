using KlaraHome.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Modules.Notifications.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="NotificationTemplate"/> to <c>notifications.notification_templates</c>.</summary>
internal sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("notification_templates", table =>
        {
            table.HasCheckConstraint(
                "ck_notification_templates_channel",
                "channel IN ('Email', 'Sms', 'WhatsApp', 'InApp')");

            table.HasCheckConstraint(
                "ck_notification_templates_category",
                "category IN ('Security', 'Orders', 'Shipping', 'Payments', 'Vendor', 'Marketing')");
        });

        builder.HasKey(template => template.Id);
        builder.Property(template => template.Id).ValueGeneratedNever();

        builder.Property(template => template.EventKey).HasMaxLength(128);
        builder.Property(template => template.Locale).HasMaxLength(16);
        builder.Property(template => template.Subject).HasMaxLength(300);
        builder.Property(template => template.ProviderTemplateId).HasMaxLength(64);

        builder.Property(template => template.Channel).HasConversion<string>().HasMaxLength(16);
        builder.Property(template => template.Category).HasConversion<string>().HasMaxLength(16);

        // One active wording per event, channel and language. Without it, "which template renders
        // this event" would be a policy about which duplicate wins.
        builder
            .HasIndex(template => new
            {
                template.TenantId,
                template.EventKey,
                template.Channel,
                template.Locale,
            })
            .IsUnique();

        builder.Ignore(template => template.DomainEvents);
    }
}

/// <summary>
/// Maps <see cref="NotificationMessage"/> to <c>notifications.notification_messages</c>.
/// </summary>
/// <remarks>
/// The table is created by hand and excluded from migrations: it is
/// <c>PARTITION BY RANGE (created_at)</c> (docs/03-database-design.md §8), and a partitioned table
/// is created partitioned or not at all — there is no migration that converts one afterwards. The
/// mapping still has to describe every column, because this is what the dispatcher and the admin
/// surface query through.
/// </remarks>
internal sealed class NotificationMessageConfiguration : IEntityTypeConfiguration<NotificationMessage>
{
    public void Configure(EntityTypeBuilder<NotificationMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("notification_messages", table => table.ExcludeFromMigrations());

        // Composite, and in this order: PostgreSQL requires the partition key to be part of every
        // unique constraint on a partitioned table.
        builder.HasKey(message => new { message.CreatedAt, message.Id });

        builder.Property(message => message.Id).ValueGeneratedNever();

        builder.Property(message => message.EventKey).HasMaxLength(128);
        builder.Property(message => message.Recipient).HasMaxLength(320);
        builder.Property(message => message.Subject).HasMaxLength(300);
        builder.Property(message => message.ProviderMessageId).HasMaxLength(128);
        builder.Property(message => message.CorrelationId).HasMaxLength(64);
        builder.Property(message => message.Error).HasMaxLength(2000);

        builder.Property(message => message.Channel).HasConversion<string>().HasMaxLength(16);
        builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(message => message.Suppression).HasConversion<string>().HasMaxLength(24);

        builder.Property(message => message.Payload).HasColumnType("jsonb");

        builder.Ignore(message => message.DomainEvents);
        builder.Ignore(message => message.IsPending);
    }
}

/// <summary>Maps <see cref="NotificationPreference"/> to <c>notifications.notification_preferences</c>.</summary>
internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("notification_preferences", table => table.HasCheckConstraint(
            "ck_notification_preferences_category",
            "category IN ('Orders', 'Shipping', 'Payments', 'Vendor', 'Marketing')"));

        builder.HasKey(preference => preference.Id);
        builder.Property(preference => preference.Id).ValueGeneratedNever();

        builder.Property(preference => preference.Category).HasConversion<string>().HasMaxLength(16);

        builder
            .HasIndex(preference => new { preference.TenantId, preference.UserId, preference.Category })
            .IsUnique();

        builder.Ignore(preference => preference.DomainEvents);
    }
}
