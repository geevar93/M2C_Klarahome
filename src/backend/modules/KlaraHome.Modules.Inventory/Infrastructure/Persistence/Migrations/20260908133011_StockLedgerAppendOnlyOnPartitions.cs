using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Inventory.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Extends the append-only guard on <c>inventory.stock_ledger_entries</c> to the partitions the
    /// rows actually live in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>StockLedgerPartitions</c> put the guard on the parent alone, and a statement-level trigger
    /// on a partitioned table is <b>not</b> inherited by its partitions — unlike a row-level one,
    /// which is. So <c>UPDATE inventory.stock_ledger_entries …</c> was refused and
    /// <c>UPDATE inventory.stock_ledger_entries_2026_09 …</c> was not, and every partition is an
    /// ordinary table whose name is exactly what a maintenance script, a repair query or a
    /// well-meaning operator reaches for.
    /// </para>
    /// <para>
    /// That mattered more here than it would elsewhere. Both quantity columns on
    /// <c>stock_items</c> are caches of sums over this table, so the nightly reconciliation compares
    /// a cache against the ledger; a ledger that could be edited would let somebody make the two
    /// agree about a number neither of them earned, and the only evidence of how the stock really
    /// moved would be gone.
    /// </para>
    /// <para>
    /// The trigger is attached to every partition that exists, and
    /// <c>ensure_stock_ledger_partition</c> is redefined to attach it to every partition it creates
    /// from now on — including the ones Step 31's maintenance job will add.
    /// </para>
    /// </remarks>
    public partial class StockLedgerAppendOnlyOnPartitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // Attaching is its own function, because it has two callers: this migration, walking the
            // partitions that already exist, and the partition-creation function below.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION inventory.guard_stock_ledger_partition(p_partition text)
                RETURNS void
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_trigger t
                        JOIN pg_class c ON c.oid = t.tgrelid
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'inventory'
                          AND c.relname = p_partition
                          AND t.tgname = 'trg_stock_ledger_entries_append_only')
                    THEN
                        EXECUTE format(
                            'CREATE TRIGGER trg_stock_ledger_entries_append_only '
                            || 'BEFORE UPDATE OR DELETE OR TRUNCATE ON inventory.%I '
                            || 'FOR EACH STATEMENT '
                            || 'EXECUTE FUNCTION inventory.stock_ledger_is_append_only()',
                            p_partition);
                    END IF;
                END;
                $function$;
                """);

            migrationBuilder.Sql("""
                DO $do$
                DECLARE
                    v_partition text;
                BEGIN
                    FOR v_partition IN
                        SELECT child.relname
                        FROM pg_inherits i
                        JOIN pg_class parent ON parent.oid = i.inhparent
                        JOIN pg_namespace n ON n.oid = parent.relnamespace
                        JOIN pg_class child ON child.oid = i.inhrelid
                        WHERE n.nspname = 'inventory'
                          AND parent.relname = 'stock_ledger_entries'
                    LOOP
                        PERFORM inventory.guard_stock_ledger_partition(v_partition);
                    END LOOP;
                END
                $do$;
                """);

            // Redefined rather than left alone: a partition created next year without the guard
            // would reopen the hole a month at a time, and nothing would say so.
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

                    PERFORM inventory.guard_stock_ledger_partition(v_name);

                    RETURN v_name;
                END;
                $function$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql("""
                DO $do$
                DECLARE
                    v_partition text;
                BEGIN
                    FOR v_partition IN
                        SELECT child.relname
                        FROM pg_inherits i
                        JOIN pg_class parent ON parent.oid = i.inhparent
                        JOIN pg_namespace n ON n.oid = parent.relnamespace
                        JOIN pg_class child ON child.oid = i.inhrelid
                        WHERE n.nspname = 'inventory'
                          AND parent.relname = 'stock_ledger_entries'
                    LOOP
                        EXECUTE format(
                            'DROP TRIGGER IF EXISTS trg_stock_ledger_entries_append_only ON inventory.%I',
                            v_partition);
                    END LOOP;
                END
                $do$;
                """);

            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS inventory.guard_stock_ledger_partition(text);");

            // Back to the definition that does not attach the guard.
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
        }
    }
}
