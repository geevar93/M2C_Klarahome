using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KlaraHome.Modules.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImpersonatedSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_user_sessions_revoked_reason",
                schema: "identity",
                table: "user_sessions");

            migrationBuilder.AddColumn<Guid>(
                name: "impersonated_by_user_id",
                schema: "identity",
                table: "user_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "impersonation_expires_at",
                schema: "identity",
                table: "user_sessions",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "impersonation_reason",
                schema: "identity",
                table: "user_sessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_sessions_impersonated_by_user_id",
                schema: "identity",
                table: "user_sessions",
                column: "impersonated_by_user_id",
                filter: "impersonated_by_user_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_sessions_revoked_reason",
                schema: "identity",
                table: "user_sessions",
                sql: "revoked_reason IS NULL OR revoked_reason IN ('SignedOut', 'SignedOutEverywhere', 'TokenReuseDetected', 'CredentialChanged', 'AccountClosed', 'ImpersonationEnded')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_user_sessions_impersonated_by_user_id",
                schema: "identity",
                table: "user_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_user_sessions_revoked_reason",
                schema: "identity",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "impersonated_by_user_id",
                schema: "identity",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "impersonation_expires_at",
                schema: "identity",
                table: "user_sessions");

            migrationBuilder.DropColumn(
                name: "impersonation_reason",
                schema: "identity",
                table: "user_sessions");

            migrationBuilder.AddCheckConstraint(
                name: "ck_user_sessions_revoked_reason",
                schema: "identity",
                table: "user_sessions",
                sql: "revoked_reason IS NULL OR revoked_reason IN ('SignedOut', 'SignedOutEverywhere', 'TokenReuseDetected', 'CredentialChanged', 'AccountClosed')");
        }
    }
}
