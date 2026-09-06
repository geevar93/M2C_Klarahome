using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Search.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Creates <c>search.search_queries</c> by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one table in this schema EF does not generate, and the mapping marks it
    /// <c>ExcludeFromMigrations</c> so it does not try. It is <c>PARTITION BY RANGE (created_at)</c>
    /// with monthly partitions (docs/03-database-design.md §4.13 and §9), and a partitioned table is
    /// created partitioned or not at all — there is no migration that converts one afterwards, which
    /// is why this is done now rather than when the volume makes it obvious. Every keystroke in the
    /// suggestion box is a candidate row, which makes this the highest-volume table outside the stock
    /// ledger.
    /// </para>
    /// <para>
    /// Updated rather than append-only, unlike <c>inventory.stock_ledger_entries</c>: a click is
    /// written onto the row the search created, minutes or hours later. So there is no append-only
    /// trigger here, and the entity is marked <c>IPartitioned</c> rather than <c>IAppendOnly</c> —
    /// both markers remove the <c>xmin</c> concurrency token, and only one of them would be true.
    /// The lost-update problem does not arise: the only update any row ever receives is its click,
    /// last one wins, and that is the intended behaviour rather than a tolerated race.
    /// </para>
    /// <para>
    /// Two years of monthly partitions are created from the month before this runs, plus a default
    /// partition so an insert can never fail because nobody created next month's. The scheduled
    /// partition-maintenance job belongs to Step 31 with the other operational jobs. Unlike the stock
    /// ledger, this table <b>is</b> retained for a limited period — one year
    /// (docs/03-database-design.md §9) — because what a shopper searched for is personal data, and
    /// detaching a partition is how that retention is honoured.
    /// </para>
    /// </remarks>
    public partial class SearchQueryPartitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql("""
                CREATE TABLE search.search_queries (
                    created_at         timestamptz  NOT NULL,
                    id                 uuid         NOT NULL,
                    tenant_id          uuid         NOT NULL,
                    query_text         varchar(200) NOT NULL,
                    normalised_query   varchar(200) NOT NULL,
                    source             varchar(16)  NOT NULL,
                    result_count       integer      NOT NULL,
                    filters            jsonb        NULL,
                    duration_ms        integer      NOT NULL,
                    customer_id        uuid         NULL,
                    session_id         varchar(64)  NULL,
                    clicked_position   integer      NULL,
                    clicked_variant_id uuid         NULL,
                    clicked_at         timestamptz  NULL,
                    CONSTRAINT pk_search_queries PRIMARY KEY (created_at, id),
                    CONSTRAINT ck_search_queries_source CHECK (source IN ('search', 'suggest')),
                    CONSTRAINT ck_search_queries_counts CHECK (
                        result_count >= 0 AND duration_ms >= 0),
                    -- A click is a position and a thing, together or not at all. Half a click is a
                    -- row that says somebody clicked the fourth result and does not say what it was,
                    -- which is the one thing this table is collected for.
                    CONSTRAINT ck_search_queries_click CHECK (
                        (clicked_position IS NULL AND clicked_variant_id IS NULL AND clicked_at IS NULL)
                        OR (clicked_position IS NOT NULL AND clicked_variant_id IS NOT NULL
                            AND clicked_at IS NOT NULL)),
                    CONSTRAINT ck_search_queries_position CHECK (
                        clicked_position IS NULL OR clicked_position >= 1)
                ) PARTITION BY RANGE (created_at);
                """);

            // Created on the parent, so every partition inherits them.
            //   the first is the merchandising report's grouping key, and the only index the two
            //     admin screens use;
            //   the second is the zero-result report, filtered because the rows that found nothing
            //     are the small and interesting slice of a table that only ever grows;
            //   the third is the click-through lookup: a click arrives with both halves of the key,
            //     and the primary key already answers it, so no third index is needed for that —
            //     this one answers "what did this session search for", which is how a support call
            //     about a lost basket is reconstructed.
            migrationBuilder.Sql("""
                CREATE INDEX ix_search_queries_normalised
                    ON search.search_queries (tenant_id, source, normalised_query, created_at DESC);

                CREATE INDEX ix_search_queries_zero_results
                    ON search.search_queries (tenant_id, created_at DESC)
                    WHERE result_count = 0;

                CREATE INDEX ix_search_queries_session
                    ON search.search_queries (tenant_id, session_id, created_at DESC)
                    WHERE session_id IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION search.ensure_search_query_partition(p_month date)
                RETURNS text
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    v_start date := date_trunc('month', p_month)::date;
                    v_end   date := (date_trunc('month', p_month) + interval '1 month')::date;
                    v_name  text := 'search_queries_' || to_char(v_start, 'YYYY_MM');
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'search' AND c.relname = v_name)
                    THEN
                        EXECUTE format(
                            'CREATE TABLE search.%I PARTITION OF search.search_queries '
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
                        PERFORM search.ensure_search_query_partition(
                            (v_first + (i || ' month')::interval)::date);
                    END LOOP;
                END
                $do$;
                """);

            // The safety net. Without it, a query recorded with a timestamp outside every partition's
            // range fails to insert — and because the recorder swallows a failed log write, the
            // symptom would be a query log that quietly stopped filling up.
            migrationBuilder.Sql("""
                CREATE TABLE search.search_queries_default
                    PARTITION OF search.search_queries DEFAULT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // The partitions and the indexes go with the table; the function is a separate object
            // that has to be named.
            migrationBuilder.Sql("DROP TABLE IF EXISTS search.search_queries CASCADE;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS search.ensure_search_query_partition(date);");
        }
    }
}
