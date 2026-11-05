using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarDealership.CoreService.Migrations
{
    /// <inheritdoc />
    public partial class RefreshTokenRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "previous_token_hash",
                table: "idn_admin_sessions",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "revoke_reason",
                table: "idn_admin_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "rotated_at",
                table: "idn_admin_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_idn_admin_sessions_previous_token_hash",
                table: "idn_admin_sessions",
                column: "previous_token_hash",
                filter: "previous_token_hash IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_idn_admin_sessions_previous_token_hash_len",
                table: "idn_admin_sessions",
                sql: "previous_token_hash IS NULL OR octet_length(previous_token_hash) = 32");

            // ADR-012: session id của ADR-005 (cookie, idle 30 phút) KHÔNG được trở thành refresh token sống 3–7 ngày.
            // Thu hồi mọi phiên cũ còn hiệu lực; người dùng chỉ cần đăng nhập lại một lần.
            migrationBuilder.Sql(
                "UPDATE idn_admin_sessions SET revoked_at = now(), revoke_reason = 'Adr012Migration' WHERE revoked_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_idn_admin_sessions_previous_token_hash",
                table: "idn_admin_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_idn_admin_sessions_previous_token_hash_len",
                table: "idn_admin_sessions");

            migrationBuilder.DropColumn(
                name: "previous_token_hash",
                table: "idn_admin_sessions");

            migrationBuilder.DropColumn(
                name: "revoke_reason",
                table: "idn_admin_sessions");

            migrationBuilder.DropColumn(
                name: "rotated_at",
                table: "idn_admin_sessions");
        }
    }
}
