using System;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarDealership.CoreService.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEntries : Migration
    {
        /// <summary>
        /// Biến môi trường chứa tên role DB mà ỨNG DỤNG (core-api) dùng. Role này KHÁC role chạy migration.
        /// </summary>
        public const string AppRoleEnvironmentVariable = "DB_APP_ROLE";

        private static readonly Regex SafeRoleName = new("^[a-z_][a-z0-9_]{0,62}$", RegexOptions.Compiled);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aud_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "text", maxLength: 100, nullable: false),
                    entity_type = table.Column<string>(type: "text", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "text", maxLength: 100, nullable: false),
                    correlation_id = table.Column<string>(type: "text", maxLength: 128, nullable: false),
                    changes = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_aud_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_aud_entries_actor_occurred_at",
                table: "aud_entries",
                columns: new[] { "actor_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_aud_entries_entity",
                table: "aud_entries",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_aud_entries_occurred_at",
                table: "aud_entries",
                column: "occurred_at",
                descending: new bool[0]);

            // Append-only thật ở tầng DB (Req 17.6): role ứng dụng chỉ INSERT + SELECT.
            AppendOnlyForAppRole(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aud_entries");
        }

        /// <summary>
        /// Đọc tên role ứng dụng từ <see cref="AppRoleEnvironmentVariable"/>.
        /// - Chưa đặt biến: bỏ qua (môi trường một role như CI/máy dev đơn giản). Môi trường thật PHẢI đặt.
        /// - Đã đặt: role phải tồn tại và KHÁC role đang chạy migration (owner bảng luôn có mọi quyền nên
        ///   REVOKE lên chính nó vô tác dụng). Sai một trong hai thì migration lỗi, không im lặng bỏ qua.
        /// REVOKE ALL (không chỉ UPDATE/DELETE) để cũng chặn TRUNCATE do default privileges cấp sẵn, rồi cấp lại đúng SELECT, INSERT.
        /// </summary>
        private static void AppendOnlyForAppRole(MigrationBuilder migrationBuilder)
        {
            var role = Environment.GetEnvironmentVariable(AppRoleEnvironmentVariable)?.Trim();
            if (string.IsNullOrEmpty(role))
            {
                return;
            }

            // Kiểm trước khi nhúng vào SQL thô: chỉ chữ thường/số/gạch dưới, tối đa 63 ký tự.
            if (!SafeRoleName.IsMatch(role))
            {
                throw new InvalidOperationException(
                    $"{AppRoleEnvironmentVariable} không hợp lệ: chỉ chấp nhận ^[a-z_][a-z0-9_]{{0,62}}$.");
            }

            migrationBuilder.Sql($$"""
                DO $$
                DECLARE
                    app_role text := '{{role}}';
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = app_role) THEN
                        RAISE EXCEPTION 'Role ung dung % khong ton tai. Tao role truoc khi chay migration.', app_role;
                    END IF;
                    IF app_role = current_user THEN
                        RAISE EXCEPTION 'Role ung dung % trung role dang chay migration; owner bang luon co moi quyen. Tach 2 role.', app_role;
                    END IF;
                    EXECUTE format('REVOKE ALL ON TABLE aud_entries FROM %I', app_role);
                    EXECUTE format('GRANT SELECT, INSERT ON TABLE aud_entries TO %I', app_role);
                END
                $$;
                """);
        }
    }
}
