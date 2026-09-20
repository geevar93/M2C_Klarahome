using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Notifications.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the read marker the in-app inbox is built on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written by hand, for the same reason the table was: <c>notification_messages</c> is mapped
    /// <c>ExcludeFromMigrations</c> because it is partitioned, so EF generates nothing for it
    /// however the mapping changes — this migration scaffolded empty and the SQL below is what
    /// makes it do anything.
    /// </para>
    /// <para>
    /// <c>ALTER TABLE</c> on a partitioned parent cascades to every partition, including the
    /// default one and any the maintenance job creates later, so this is one statement rather than
    /// twenty-six and stays correct for next year's partitions.
    /// </para>
    /// <para>
    /// The index is partial, and that is the whole point of it. The only question this column is
    /// ever asked is "what has this person not read yet", against a table that grows without bound
    /// and is nine-tenths email. An index over every row would be almost entirely made of messages
    /// no in-app inbox will ever ask about.
    /// </para>
    /// </remarks>
    public partial class NotificationMessageReadState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql("""
                ALTER TABLE notifications.notification_messages
                    ADD COLUMN IF NOT EXISTS read_at timestamptz NULL;
                """);

            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_notification_messages_unread
                    ON notifications.notification_messages (tenant_id, user_id, created_at DESC)
                    WHERE channel = 'InApp' AND read_at IS NULL;
                """);

            // Marking one message read names it by id and nothing else, which without this is a
            // sequential scan of every partition in the table - the primary key leads on
            // created_at, and the caller has no reason to know a partition key.
            migrationBuilder.Sql("""
                CREATE INDEX IF NOT EXISTS ix_notification_messages_id
                    ON notifications.notification_messages (id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql("DROP INDEX IF EXISTS notifications.ix_notification_messages_id;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS notifications.ix_notification_messages_unread;");

            migrationBuilder.Sql("""
                ALTER TABLE notifications.notification_messages
                    DROP COLUMN IF EXISTS read_at;
                """);
        }
    }
}
