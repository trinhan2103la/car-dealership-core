using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CarDealership.CoreService.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogAggregates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cat_car_models",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    search_text = table.Column<string>(type: "text", nullable: false),
                    min_published_price = table.Column<long>(type: "bigint", nullable: true),
                    max_published_price = table.Column<long>(type: "bigint", nullable: true),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_featured = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    display_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
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
                    table.PrimaryKey("PK_cat_car_models", x => x.id);
                    table.CheckConstraint("ck_cat_car_models_display_order", "display_order BETWEEN 0 AND 9999");
                    table.CheckConstraint("ck_cat_car_models_slug", "slug ~ '^[a-z0-9]([a-z0-9-]*[a-z0-9])?$' AND length(slug) <= 100");
                    table.CheckConstraint("ck_cat_car_models_status", "status IN ('Draft', 'Published', 'Hidden', 'Archived')");
                    table.ForeignKey(
                        name: "FK_cat_car_models_cat_brands_brand_id",
                        column: x => x.brand_id,
                        principalTable: "cat_brands",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cat_slug_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_slug = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cat_slug_history", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cat_car_variants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    car_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    reference_price = table.Column<long>(type: "bigint", nullable: true),
                    contact_for_price = table.Column<bool>(type: "boolean", nullable: false),
                    body_type = table.Column<string>(type: "text", nullable: false),
                    fuel = table.Column<string>(type: "text", nullable: false),
                    transmission = table.Column<string>(type: "text", nullable: false),
                    seats = table.Column<short>(type: "smallint", nullable: false),
                    engine = table.Column<string>(type: "text", nullable: true),
                    power = table.Column<string>(type: "text", nullable: true),
                    extra_specs = table.Column<string>(type: "jsonb", nullable: false),
                    is_demo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    concurrency_stamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cat_car_variants", x => x.id);
                    table.CheckConstraint("ck_cat_car_variants_price", "(contact_for_price AND reference_price IS NULL) OR (NOT contact_for_price AND reference_price BETWEEN 1 AND 100000000000)");
                    table.CheckConstraint("ck_cat_car_variants_seats", "seats BETWEEN 2 AND 16");
                    table.CheckConstraint("ck_cat_car_variants_status", "status IN ('Draft', 'Published', 'Hidden', 'Archived')");
                    table.ForeignKey(
                        name: "FK_cat_car_variants_cat_car_models_car_model_id",
                        column: x => x.car_model_id,
                        principalTable: "cat_car_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cat_vehicle_images",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    car_model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    car_variant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    storage_key = table.Column<string>(type: "text", nullable: false),
                    alt = table.Column<string>(type: "text", nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    is_cover = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cat_vehicle_images", x => x.id);
                    table.ForeignKey(
                        name: "FK_cat_vehicle_images_cat_car_models_car_model_id",
                        column: x => x.car_model_id,
                        principalTable: "cat_car_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_cat_vehicle_images_cat_car_variants_car_variant_id",
                        column: x => x.car_variant_id,
                        principalTable: "cat_car_variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cat_car_models_min_price",
                table: "cat_car_models",
                columns: new[] { "min_published_price", "slug" });

            migrationBuilder.CreateIndex(
                name: "ix_cat_car_models_published",
                table: "cat_car_models",
                columns: new[] { "status", "published_at", "slug" },
                descending: new[] { false, true, false },
                filter: "status = 'Published'");

            migrationBuilder.CreateIndex(
                name: "ux_cat_car_models_brand_slug",
                table: "cat_car_models",
                columns: new[] { "brand_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cat_car_variants_model_status",
                table: "cat_car_variants",
                columns: new[] { "car_model_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_cat_slug_history_entity",
                table: "cat_slug_history",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ux_cat_slug_history_old_slug",
                table: "cat_slug_history",
                columns: new[] { "entity_type", "scope_id", "old_slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cat_vehicle_images_car_variant_id",
                table: "cat_vehicle_images",
                column: "car_variant_id");

            migrationBuilder.CreateIndex(
                name: "ix_cat_vehicle_images_model_order",
                table: "cat_vehicle_images",
                columns: new[] { "car_model_id", "display_order" });

            migrationBuilder.CreateIndex(
                name: "ux_cat_vehicle_images_cover",
                table: "cat_vehicle_images",
                column: "car_model_id",
                unique: true,
                filter: "is_cover");

            // ---- SQL thô: EF không mô tả được index theo biểu thức và extension ----

            // Tên duy nhất không phân biệt hoa thường: CarModel trong Brand, CarVariant trong CarModel (Brand ở AddBrands).
            // DB là nơi quyết định cuối khi hai request trùng tên đến đồng thời (Req 7.4, 8.2, 8.3).
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_cat_car_models_brand_name ON cat_car_models (brand_id, lower(name));");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_cat_car_variants_model_name ON cat_car_variants (car_model_id, lower(name));");

            // Tìm kiếm không dấu bằng ILIKE '%…%' trên search_text (đã chuẩn hóa bằng C#): GIN + gin_trgm_ops.
            // Supabase đặt extension trong schema "extensions"; kiểm lại operator class qua search_path khi migrate demo.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");
            migrationBuilder.Sql("CREATE INDEX ix_cat_car_models_search_text ON cat_car_models USING gin (search_text gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cat_slug_history");

            migrationBuilder.DropTable(
                name: "cat_vehicle_images");

            migrationBuilder.DropTable(
                name: "cat_car_variants");

            migrationBuilder.DropTable(
                name: "cat_car_models");
        }
    }
}
