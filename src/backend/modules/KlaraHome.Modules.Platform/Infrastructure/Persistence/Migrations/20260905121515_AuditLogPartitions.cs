using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Platform.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Creates <c>platform.audit_logs</c> by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one table in the schema EF does not generate, and the mapping marks it
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
    ///     outright, so the guarantee holds against <c>psql</c> and not only against our own code
    ///     (docs/07-security-compliance.md §7);
    ///   </description></item>
    ///   <item><description>
    ///     it carries a <c>DEFAULT</c> partition, so an insert can never fail because nobody
    ///     created next month's.
    ///   </description></item>
    /// </list>
    /// <para>
    /// The migration creates two years of monthly partitions from the month before it runs. When
    /// that window runs out, rows land in the default partition and everything keeps working, but
    /// queries stop being pruned — and a later attempt to create that month's partition will fail
    /// while its rows sit in the default. Draining it is a
    /// <c>BEGIN; CREATE TABLE ... (LIKE ...); ... ; END;</c> exercise, which is exactly why the
    /// scheduled partition-maintenance job belongs to Step 31 rather than to a human's memory.
    /// </para>
    /// </remarks>
    public partial class AuditLogPartitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            migrationBuilder.Sql("""
                CREATE TABLE platform.audit_logs (
                    occurred_at    timestamptz   NOT NULL,
                    id             uuid          NOT NULL,
                    tenant_id      uuid          NOT NULL,
                    actor_id       uuid          NULL,
                    actor_type     varchar(32)   NOT NULL,
                    action         varchar(128)  NOT NULL,
                    entity_type    varchar(128)  NOT NULL,
                    entity_id      varchar(128)  NULL,
                    before         jsonb         NULL,
                    after          jsonb         NULL,
                    ip             varchar(45)   NULL,
                    user_agent     varchar(512)  NULL,
                    correlation_id varchar(64)   NULL,
                    CONSTRAINT pk_audit_logs PRIMARY KEY (occurred_at, id),
                    CONSTRAINT ck_audit_logs_actor_type CHECK (
                        actor_type IN ('System', 'Anonymous', 'Customer', 'VendorUser', 'StaffUser'))
                ) PARTITION BY RANGE (occurred_at);
                """);

            // Every query the admin surface issues is (tenant, one filter, newest first), which is
            // what these three cover. Created on the parent, so every partition inherits them.
            migrationBuilder.Sql("""
                CREATE INDEX ix_audit_logs_tenant_id
                    ON platform.audit_logs (tenant_id);

                CREATE INDEX ix_audit_logs_tenant_entity
                    ON platform.audit_logs (tenant_id, entity_type, entity_id, occurred_at DESC);

                CREATE INDEX ix_audit_logs_tenant_actor
                    ON platform.audit_logs (tenant_id, actor_id, occurred_at DESC);

                CREATE INDEX ix_audit_logs_tenant_action
                    ON platform.audit_logs (tenant_id, action, occurred_at DESC);
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION platform.ensure_audit_log_partition(p_month date)
                RETURNS text
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    v_start date := date_trunc('month', p_month)::date;
                    v_end   date := (date_trunc('month', p_month) + interval '1 month')::date;
                    v_name  text := 'audit_logs_' || to_char(v_start, 'YYYY_MM');
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'platform' AND c.relname = v_name)
                    THEN
                        EXECUTE format(
                            'CREATE TABLE platform.%I PARTITION OF platform.audit_logs '
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
                        PERFORM platform.ensure_audit_log_partition(
                            (v_first + (i || ' month')::interval)::date);
                    END LOOP;
                END
                $do$;
                """);

            // The safety net. Without it, an insert whose timestamp falls outside every partition
            // fails - and the operation being audited would fail with it.
            migrationBuilder.Sql("""
                CREATE TABLE platform.audit_logs_default
                    PARTITION OF platform.audit_logs DEFAULT;
                """);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION platform.audit_logs_reject_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'platform.audit_logs is append-only; % is not permitted', TG_OP
                        USING ERRCODE = 'insufficient_privilege';
                END;
                $function$;

                CREATE TRIGGER trg_audit_logs_append_only
                    BEFORE UPDATE OR DELETE ON platform.audit_logs
                    FOR EACH ROW
                    EXECUTE FUNCTION platform.audit_logs_reject_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            ArgumentNullException.ThrowIfNull(migrationBuilder);

            // The partitions and the trigger go with the table; only the two functions are separate
            // objects that have to be named.
            migrationBuilder.Sql("DROP TABLE IF EXISTS platform.audit_logs CASCADE;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS platform.audit_logs_reject_mutation();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS platform.ensure_audit_log_partition(date);");
        }
    }
}
