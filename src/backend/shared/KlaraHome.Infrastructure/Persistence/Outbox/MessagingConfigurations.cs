using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Infrastructure.Persistence.Outbox;

/// <summary>
/// Where the messaging tables live. The <c>platform</c> schema per docs/03-database-design.md
/// §4.1, named explicitly rather than inherited from the context's default schema — every module
/// context maps these tables, and they must all resolve to the one queue.
/// </summary>
/// <remarks>
/// This is the single sanctioned cross-schema write in the system, and it is not a boundary
/// violation: the module-boundary rule forbids reading another module's <em>business</em> tables
/// and joining across schemas (docs/01-architecture.md §2.1). The outbox is infrastructure, has
/// no foreign keys, and is never queried by a module — a module only ever appends to it, in its
/// own transaction, which is precisely what makes the pattern work.
/// </remarks>
public static class MessagingTables
{
    /// <summary>The schema that holds the outbox and inbox.</summary>
    public const string Schema = "platform";

    /// <summary>Pending and dispatched integration events.</summary>
    public const string Outbox = "outbox_messages";

    /// <summary>Handler completions, for idempotent redelivery.</summary>
    public const string Inbox = "inbox_messages";
}

/// <param name="ownsTable">
/// Whether this context carries the table's DDL. False for every context but one, so the table is
/// mapped for writing without each module's migrations trying to create it.
/// </param>
internal sealed class OutboxMessageConfiguration(bool ownsTable) : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(MessagingTables.Outbox, MessagingTables.Schema, table =>
        {
            if (!ownsTable)
            {
                table.ExcludeFromMigrations();
            }
        });

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();

        builder.Property(message => message.Type).HasMaxLength(256);
        builder.Property(message => message.Payload).HasColumnType("jsonb");
        builder.Property(message => message.CorrelationId).HasMaxLength(64);
        builder.Property(message => message.Error).HasMaxLength(4000);

        // The dispatcher's only query. A partial index keeps it the size of the backlog rather
        // than the size of the history, so polling stays cheap as the table grows.
        builder
            .HasIndex(message => message.OccurredAt)
            .HasDatabaseName("ix_outbox_messages_pending")
            .HasFilter("processed_at IS NULL");
    }
}

/// <param name="ownsTable">Whether this context carries the table's DDL.</param>
internal sealed class InboxMessageConfiguration(bool ownsTable) : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(MessagingTables.Inbox, MessagingTables.Schema, table =>
        {
            if (!ownsTable)
            {
                table.ExcludeFromMigrations();
            }
        });

        // The composite key is the idempotency guarantee: inserting it twice is a primary-key
        // violation, which is exactly the answer "this handler has already run" needs.
        builder.HasKey(message => new { message.MessageId, message.Handler });
        builder.Property(message => message.Handler).HasMaxLength(256);
    }
}
