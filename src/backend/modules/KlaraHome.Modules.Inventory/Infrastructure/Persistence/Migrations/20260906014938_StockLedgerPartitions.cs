using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Creates <c>inventory.stock_ledger_entries</c> by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one table in this schema EF does not generate, and the mapping marks it
    /// <c>ExcludeFromMigrations</c> so it does not try. It is
    /// <c>PARTITION BY RANGE (occurred_at)</c> with monthly partitions
    /// (docs/03-database-design.md §4.5), and a partitioned table is created partitioned or not at
    /// all — there is no migration that converts one afterwards, which is why this is done now
    /// rather than when the volume makes it obvious. This is the highest-volume table in the
    /// commerce spine: every sale, hold, release and expiry writes one row.
    /// </para>
    /// <para>
    /// Append-only and enforced as such by a trigger, unlike
    /// <c>notifications.notification_messages</c>, which is partitioned for the same reason but
    /// <em>is</em> updated. Both quantity columns on <c>stock_items</c> are caches of sums over this
    /// table, so a ledger that could be edited would make the nightly reconciliation meaningless —
    /// it would be comparing a cache against something equally mutable.
    /// </para>
    /// <para>
    /// Two years of monthly partitions are created from the month before this runs, plus a default
    /// partition so an insert can never fail because nobody created next month's. The scheduled
    /// partition-maintenance job belongs to Step 31 with the other operational jobs. Unlike message
    /// bodies, ledger entries have <b>no retention limit</b>: they are the record of truth behind
    /// every quantity this platform reports, and dropping an old partition would make the oldest
    /// stock rows unreconcilable.
    /// </para>
    /// </remarks>
    public partial class StockLedgerPartitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql("""
                CREATE TABLE inventory.stock_ledger_entries (
                    occurred_at      timestamptz  NOT NULL,
                    id               uuid         NOT NULL,
                    tenant_id        uuid         NOT NULL,
                    stock_item_id    uuid         NOT NULL,
                    change           integer      NOT NULL,
                    balance_after    integer      NOT NULL,
                    reserved_change  integer      NOT NULL,
                    reserved_after   integer      NOT NULL,
                    reason           varchar(16)  NOT NULL,
                    reference_type   varchar(32)  NULL,
                    reference_id     uuid         NULL,
                    note             varchar(500) NULL,
                    actor_id         uuid         NULL,
                    CONSTRAINT pk_stock_ledger_entries PRIMARY KEY (occurred_at, id),
                    CONSTRAINT ck_stock_ledger_entries_reason CHECK (
                        reason IN ('Purchase', 'Sale', 'Reservation', 'Release', 'Return',
                                   'Adjustment', 'Damage', 'TransferIn', 'TransferOut', 'Correction')),
                    -- An entry that moves nothing is noise in the one table that has to stay
                    -- readable, and it is always a bug in the caller rather than a real movement.
                    CONSTRAINT ck_stock_ledger_entries_moves_something CHECK (
                        change <> 0 OR reserved_change <> 0),
                    -- The balances are what the conditional UPDATE returned, so they can never be
                    -- negative. If one ever is, the write path is wrong and this is where it stops.
                    CONSTRAINT ck_stock_ledger_entries_balance CHECK (balance_after >= 0),
                    CONSTRAINT ck_stock_ledger_entries_reserved CHECK (reserved_after >= 0)
                ) PARTITION BY RANGE (occurred_at);
                """);

            // Created on the parent, so every partition inherits them.
            //   the first is the hot one: one stock row's movements, newest first, which is both
            //   the admin ledger screen and the reconciliation job's grouping key;
            //   the second answers "what did that order do to stock", which is the question every
            //   support conversation about a missing unit turns into.
            migrationBuilder.Sql("""
                CREATE INDEX ix_stock_ledger_entries_item
                    ON inventory.stock_ledger_entries (stock_item_id, occurred_at DESC);

                CREATE INDEX ix_stock_ledger_entries_reference
                    ON inventory.stock_ledger_entries (tenant_id, reference_type, reference_id);

                CREATE INDEX ix_stock_ledger_entries_tenant_occurred
                    ON inventory.stock_ledger_entries (tenant_id, occurred_at DESC);
                """);

            // Append-only, enforced rather than agreed. A ledger somebody can edit is not a ledger,
            // and the whole reconciliation story rests on this being true.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION inventory.stock_ledger_is_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION
                        'inventory.stock_ledger_entries is append-only; % is not permitted', TG_OP
                        USING ERRCODE = '0A000';
                END;
                $function$;

                CREATE TRIGGER trg_stock_ledger_entries_append_only
                    BEFORE UPDATE OR DELETE OR TRUNCATE ON inventory.stock_ledger_entries
                    FOR EACH STATEMENT
                    EXECUTE FUNCTION inventory.stock_ledger_is_append_only();
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION inventory.ensure_stock_ledger_partition(p_month date)
                RETURNS text
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    v_start date := date_trunc('month', p_month)::date;
                    v_end   date := (date_trunc('month', p_month) + interval '1 month')::date;
                    v_name  text := 'stock_ledger_entries_' || to_char(v_start, 'YYYY_MM');
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'inventory' AND c.relname = v_name)
                    THEN
                        EXECUTE format(
                            'CREATE TABLE inventory.%I PARTITION OF inventory.stock_ledger_entries '
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
                        PERFORM inventory.ensure_stock_ledger_partition(
                            (v_first + (i || ' month')::interval)::date);
                    END LOOP;
                END
                $do$;
                """);

            // The safety net. Without it, a movement dated outside every partition's range fails to
            // insert - and the sale that caused it would fail with it.
            migrationBuilder.Sql("""
                CREATE TABLE inventory.stock_ledger_entries_default
                    PARTITION OF inventory.stock_ledger_entries DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // The partitions, the indexes and the trigger go with the table; the two functions are
            // separate objects that have to be named.
            migrationBuilder.Sql("DROP TABLE IF EXISTS inventory.stock_ledger_entries CASCADE;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS inventory.stock_ledger_is_append_only();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS inventory.ensure_stock_ledger_partition(date);");
        }
    }
}
