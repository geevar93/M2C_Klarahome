using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Notifications.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Creates <c>notifications.notification_messages</c> by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one table in this schema EF does not generate, and the mapping marks it
    /// <c>ExcludeFromMigrations</c> so it does not try. It is
    /// <c>PARTITION BY RANGE (created_at)</c> with monthly partitions
    /// (docs/03-database-design.md §8), and a partitioned table is created partitioned or not at
    /// all — there is no migration that converts one afterwards, which is why this is done now
    /// rather than when the volume makes it obvious.
    /// </para>
    /// <para>
    /// Unlike <c>platform.audit_logs</c>, which is partitioned for the same reason, this table
    /// <em>is</em> updated: a message moves from queued to sent. So there is no append-only
    /// trigger. What there also cannot be is an <c>xmin</c> concurrency token — PostgreSQL refuses
    /// to return a system column from a partitioned table — so the row is marked
    /// <c>IPartitioned</c> and concurrency is handled pessimistically instead, with
    /// <c>FOR UPDATE SKIP LOCKED</c> in the dispatcher.
    /// </para>
    /// <para>
    /// Two years of monthly partitions are created from the month before this runs, plus a default
    /// partition so an insert can never fail because nobody created next month's. The scheduled
    /// partition-maintenance job — and the ninety-day retention §8 requires for message bodies —
    /// belong to Step 31 with the other operational jobs.
    /// </para>
    /// </remarks>
    public partial class NotificationMessagePartitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql("""
                CREATE TABLE notifications.notification_messages (
                    created_at         timestamptz  NOT NULL,
                    id                 uuid         NOT NULL,
                    tenant_id          uuid         NOT NULL,
                    event_key          varchar(128) NOT NULL,
                    channel            varchar(16)  NOT NULL,
                    template_id        uuid         NULL,
                    template_version   integer      NULL,
                    user_id            uuid         NULL,
                    recipient          varchar(320) NOT NULL,
                    subject            varchar(300) NULL,
                    body               text         NULL,
                    payload            jsonb        NULL,
                    status             varchar(16)  NOT NULL,
                    suppression        varchar(24)  NOT NULL,
                    provider_message_id varchar(128) NULL,
                    attempts           integer      NOT NULL,
                    next_attempt_at    timestamptz  NULL,
                    error              varchar(2000) NULL,
                    sent_at            timestamptz  NULL,
                    delivered_at       timestamptz  NULL,
                    correlation_id     varchar(64)  NULL,
                    CONSTRAINT pk_notification_messages PRIMARY KEY (created_at, id),
                    CONSTRAINT ck_notification_messages_channel CHECK (
                        channel IN ('Email', 'Sms', 'WhatsApp', 'InApp')),
                    CONSTRAINT ck_notification_messages_status CHECK (
                        status IN ('Queued', 'Sending', 'Sent', 'Delivered', 'Failed', 'Bounced', 'Suppressed')),
                    CONSTRAINT ck_notification_messages_suppression CHECK (
                        suppression IN ('None', 'NoProvider', 'ChannelDisabled', 'OptedOut',
                                        'NoRecipient', 'NoTemplate')),
                    CONSTRAINT ck_notification_messages_attempts CHECK (attempts >= 0),
                    -- A suppressed message is one nobody was asked about, so it must carry a
                    -- reason; anything else must not. Without this the two states blur, and
                    -- "how many messages could we not send" stops being answerable.
                    CONSTRAINT ck_notification_messages_suppression_reason CHECK (
                        (status = 'Suppressed') = (suppression <> 'None'))
                ) PARTITION BY RANGE (created_at);
                """);

            // Created on the parent, so every partition inherits them.
            //   the first is the dispatcher's claim query, which is the hot one;
            //   the second and third are the two ways the admin surface filters the log.
            migrationBuilder.Sql("""
                CREATE INDEX ix_notification_messages_due
                    ON notifications.notification_messages (status, next_attempt_at);

                CREATE INDEX ix_notification_messages_tenant_created
                    ON notifications.notification_messages (tenant_id, created_at DESC);

                CREATE INDEX ix_notification_messages_tenant_event
                    ON notifications.notification_messages (tenant_id, event_key, created_at DESC);

                CREATE INDEX ix_notification_messages_tenant_user
                    ON notifications.notification_messages (tenant_id, user_id, created_at DESC);
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION notifications.ensure_notification_message_partition(p_month date)
                RETURNS text
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    v_start date := date_trunc('month', p_month)::date;
                    v_end   date := (date_trunc('month', p_month) + interval '1 month')::date;
                    v_name  text := 'notification_messages_' || to_char(v_start, 'YYYY_MM');
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'notifications' AND c.relname = v_name)
                    THEN
                        EXECUTE format(
                            'CREATE TABLE notifications.%I PARTITION OF notifications.notification_messages '
                            || 'FOR VALUES FROM (%L) TO (%L)',
                            v_name, v_start, v_end);
                    END IF;

                    RETURN v_name;
                END;
                $function$;
                """);

            migrationBuilder.Sql("""
                DO $do$
                DECLARE
                    v_first date := (date_trunc('month', now()) - interval '1 month')::date;
                BEGIN
                    FOR i IN 0..24 LOOP
                        PERFORM notifications.ensure_notification_message_partition(
                            (v_first + (i || ' month')::interval)::date);
                    END LOOP;
                END
                $do$;
                """);

            // The safety net. Without it, a message queued outside every partition's range fails to
            // insert - and the operation that wanted to notify somebody would fail with it.
            migrationBuilder.Sql("""
                CREATE TABLE notifications.notification_messages_default
                    PARTITION OF notifications.notification_messages DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // The partitions and the indexes go with the table; only the function is a separate
            // object that has to be named.
            migrationBuilder.Sql("DROP TABLE IF EXISTS notifications.notification_messages CASCADE;");
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS notifications.ensure_notification_message_partition(date);");
        }
    }
}
