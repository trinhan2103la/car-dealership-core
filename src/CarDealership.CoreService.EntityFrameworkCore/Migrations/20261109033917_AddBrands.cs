using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarDealership.CoreService.Migrations
{
    /// <inheritdoc />
    public partial class AddBrands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cat_brands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    logo_key = table.Column<string>(type: "text", nullable: true),
                    is_demo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    extra_properties = table.Column<string>(type: "text", nullable: false),
                    concurrency_stamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    creation_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    creator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_modification_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_modifier_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cat_brands", x => x.id);
                    table.CheckConstraint("ck_cat_brands_display_order", "display_order BETWEEN 0 AND 9999");
                    table.CheckConstraint("ck_cat_brands_slug", "slug ~ '^[a-z0-9]([a-z0-9-]*[a-z0-9])?$' AND length(slug) <= 100");
                });

            migrationBuilder.CreateIndex(
                name: "ux_cat_brands_slug",
                table: "cat_brands",
                column: "slug",
                unique: true);

            // UNIQUE (lower(name)): index biểu thức, EF không mô tả được nên tạo bằng SQL thô.
            // Là nơi quyết định cuối khi hai request trùng tên (kể cả khác hoa thường) đến đồng thời (Req 7.3, 7.4).
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_cat_brands_name ON cat_brands (lower(name));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cat_brands");
        }
    }
}
