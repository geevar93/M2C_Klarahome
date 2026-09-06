using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Shipping.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Creates <c>shipping.tracking_events</c> by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one table in this schema EF does not generate, and the mapping marks it
    /// <c>ExcludeFromMigrations</c> so it does not try. Three of its properties cannot be expressed
    /// in the EF model at all:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     it is <c>PARTITION BY RANGE (occurred_at)</c> with monthly partitions
    ///     (docs/03-database-design.md §8), and a partitioned table is created partitioned or not
    ///     at all — there is no migration that converts one later;
    ///   </description></item>
    ///   <item><description>
    ///     it is append-only, enforced by a trigger that rejects <c>UPDATE</c> and <c>DELETE</c>
    ///     outright, so the guarantee holds against <c>psql</c> and not only against our own code —
    ///     a courier scan is evidence, and evidence that can be edited is not evidence;
    ///   </description></item>
    ///   <item><description>
    ///     it carries a <c>DEFAULT</c> partition, so an insert can never fail because nobody
    ///     created next month's — and a tracking insert that failed would lose a courier's word.
    ///   </description></item>
    /// </list>
    /// <para>
    /// The same shape as <c>platform.audit_logs</c> and <c>inventory.stock_ledger_entries</c>, and
    /// deliberately so: three tables that are written constantly, read by range and never updated
    /// should not be three different designs.
    /// </para>
    /// <para>
    /// Two years of monthly partitions are created from the month before it runs. When that window
    /// runs out, rows land in the default partition and everything keeps working, but queries stop
    /// being pruned — and a later attempt to create that month's partition will fail while its rows
    /// sit in the default. The scheduled partition-maintenance job is Step 31's, and this table
    /// joins the two already waiting for it.
    /// </para>
    /// </remarks>
    public partial class TrackingEventPartitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql("""
                CREATE TABLE shipping.tracking_events (
                    occurred_at       timestamptz            NOT NULL,
                    id                uuid                   NOT NULL,
                    tenant_id         uuid                   NOT NULL,
                    shipment_id       uuid                   NOT NULL,
                    provider_event_id character varying(128) NOT NULL,
                    status            character varying(24)  NOT NULL,
                    courier_status    character varying(64)  NULL,
                    location          character varying(128) NULL,
                    remark            character varying(500) NULL,
                    is_applied        boolean                NOT NULL DEFAULT false,
                    received_at       timestamptz            NOT NULL,
                    raw               jsonb                  NULL,
                    CONSTRAINT pk_tracking_events PRIMARY KEY (occurred_at, id),
                    CONSTRAINT ck_tracking_events_status CHECK (
                        status IN ('Draft', 'Created', 'LabelGenerated', 'PickupScheduled', 'PickedUp',
                                   'InTransit', 'OutForDelivery', 'Delivered', 'Exception',
                                   'RtoInitiated', 'RtoDelivered', 'Cancelled'))
                ) PARTITION BY RANGE (occurred_at);
                """);

            // The deduplication that makes a redelivered webhook free. It has to include the
            // partition key: PostgreSQL requires it in every unique constraint on a partitioned
            // table, which is why this is (shipment, event, instant) rather than (shipment, event).
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_tracking_events_shipment_event
                    ON shipping.tracking_events (shipment_id, provider_event_id, occurred_at);

                CREATE INDEX ix_tracking_events_tenant_id
                    ON shipping.tracking_events (tenant_id);

                CREATE INDEX ix_tracking_events_shipment
                    ON shipping.tracking_events (shipment_id, occurred_at DESC);
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION shipping.ensure_tracking_event_partition(p_month date)
                RETURNS text
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    v_start date := date_trunc('month', p_month)::date;
                    v_end   date := (date_trunc('month', p_month) + interval '1 month')::date;
                    v_name  text := 'tracking_events_' || to_char(v_start, 'YYYY_MM');
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'shipping' AND c.relname = v_name)
                    THEN
                        EXECUTE format(
                            'CREATE TABLE shipping.%I PARTITION OF shipping.tracking_events '
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
                        PERFORM shipping.ensure_tracking_event_partition(
                            (v_first + (i || ' month')::interval)::date);
                    END LOOP;
                END
                $do$;
                """);

            // The safety net. A courier scan whose timestamp falls outside every partition must land
            // somewhere: the alternative is a webhook that fails and a parcel nobody can track.
            migrationBuilder.Sql("""
                CREATE TABLE shipping.tracking_events_default
                    PARTITION OF shipping.tracking_events DEFAULT;
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION shipping.tracking_events_reject_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'shipping.tracking_events is append-only; % is not permitted', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $function$;

                CREATE TRIGGER trg_tracking_events_append_only
                    BEFORE UPDATE OR DELETE ON shipping.tracking_events
                    FOR EACH ROW
                    EXECUTE FUNCTION shipping.tracking_events_reject_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // The partitions and the trigger go with the table; only the two functions are separate
            // objects that have to be named.
            migrationBuilder.Sql("DROP TABLE IF EXISTS shipping.tracking_events CASCADE;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS shipping.tracking_events_reject_mutation();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS shipping.ensure_tracking_event_partition(date);");
        }
    }
}
