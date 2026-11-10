using System;
using System.Collections.Generic;
using CarDealership.CoreService.Catalog;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Slugs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace CarDealership.CoreService.EntityFrameworkCore.Catalog;

/// <summary>
/// Cáº¥u hĂ¬nh EF Core cho module Catalog. Báº£ng dĂ¹ng tiá»n tá»‘ "cat_", tĂªn báº£ng/cá»™t snake_case ghi TÆ¯á»œNG MINH
/// (khĂ´ng dĂ¹ng naming convention toĂ n DbContext Ä‘á»ƒ khĂ´ng Ä‘á»•i tĂªn báº£ng cá»§a ABP).
/// Index theo biá»ƒu thá»©c (<c>lower(name)</c>) vĂ  extension PostgreSQL náº±m trong migration (SQL thĂ´) vĂ¬ EF khĂ´ng mĂ´ táº£ Ä‘Æ°á»£c.
/// </summary>
public static class CatalogDbContextModelCreatingExtensions
{
    public const string TablePrefix = "cat_";

    public static void ConfigureCatalog(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));

        builder.Entity<Brand>(b =>
        {
            b.ToTable(TablePrefix + "brands", t =>
            {
                t.HasCheckConstraint("ck_cat_brands_slug", "slug ~ '^[a-z0-9]([a-z0-9-]*[a-z0-9])?$' AND length(slug) <= 100");
                t.HasCheckConstraint("ck_cat_brands_display_order", "display_order BETWEEN 0 AND 9999");
            });

            b.ConfigureByConvention();
            b.ConfigureAuditedColumns();

            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.Name).HasColumnName("name").HasColumnType("text").IsRequired();
            b.Property(x => x.Slug).HasColumnName("slug").HasColumnType("text").IsRequired();
            b.Property(x => x.Status).HasColumnName("status").HasColumnType("text").HasConversion<string>().IsRequired();
            b.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
            b.Property(x => x.LogoKey).HasColumnName("logo_key").HasColumnType("text");
            b.Property(x => x.IsDemo).HasColumnName("is_demo").HasDefaultValue(false).IsRequired();

            // UNIQUE (slug). UNIQUE (lower(name)) lĂ  index biá»ƒu thá»©c, táº¡o báº±ng SQL thĂ´ trong migration (ux_cat_brands_name).
            b.HasIndex(x => x.Slug).IsUnique().HasDatabaseName(CatalogConstraints.BrandSlug);
        });

        ConfigureCarModel(builder);
        ConfigureCarVariant(builder);
        ConfigureVehicleImage(builder);
        ConfigureSlugHistory(builder);
    }

    private static void ConfigureCarModel(ModelBuilder builder)
    {
        builder.Entity<CarModel>(b =>
        {
            b.ToTable(TablePrefix + "car_models", t =>
            {
                t.HasCheckConstraint("ck_cat_car_models_slug", "slug ~ '^[a-z0-9]([a-z0-9-]*[a-z0-9])?$' AND length(slug) <= 100");
                t.HasCheckConstraint("ck_cat_car_models_display_order", "display_order BETWEEN 0 AND 9999");
                t.HasCheckConstraint("ck_cat_car_models_status", "status IN ('Draft', 'Published', 'Hidden', 'Archived')");
            });

            b.ConfigureByConvention();
            b.ConfigureAuditedColumns();

            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.BrandId).HasColumnName("brand_id").IsRequired();
            b.Property(x => x.Name).HasColumnName("name").HasColumnType("text").IsRequired();
            b.Property(x => x.Slug).HasColumnName("slug").HasColumnType("text").IsRequired();
            b.Property(x => x.Description).HasColumnName("description").HasColumnType("text");
            b.Property(x => x.Status).HasColumnName("status").HasColumnType("text").HasConversion<string>().IsRequired();
            b.Property(x => x.SearchText).HasColumnName("search_text").HasColumnType("text").IsRequired();
            b.Property(x => x.MinPublishedPrice).HasColumnName("min_published_price").HasColumnType("bigint");
            b.Property(x => x.MaxPublishedPrice).HasColumnName("max_published_price").HasColumnType("bigint");
            b.Property(x => x.PublishedAt).HasColumnName("published_at").HasColumnType("timestamp with time zone")
                .HasConversion(v => v, v => v.HasValue ? v.Value.ToUniversalTime() : v);
            b.Property(x => x.IsFeatured).HasColumnName("is_featured").HasDefaultValue(false).IsRequired();
            b.Property(x => x.DisplayOrder).HasColumnName("display_order").HasDefaultValue(0).IsRequired();
            b.Property(x => x.IsDemo).HasColumnName("is_demo").HasDefaultValue(false).IsRequired();

            b.Ignore(x => x.HasEverBeenPublished);

            // Brand lĂ  aggregate khĂ¡c trong CĂ™NG module nĂªn cĂ³ FK; CarModel khĂ´ng cĂ³ navigation sang Brand.
            b.HasOne<Brand>().WithMany().HasForeignKey(x => x.BrandId).OnDelete(DeleteBehavior.Restrict);

            b.HasMany(x => x.Variants).WithOne().HasForeignKey(v => v.CarModelId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Images).WithOne().HasForeignKey(i => i.CarModelId).OnDelete(DeleteBehavior.Cascade);
            b.Navigation(x => x.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
            b.Navigation(x => x.Images).UsePropertyAccessMode(PropertyAccessMode.Field);

            // Slug duy nháº¥t TRONG Brand: hai Brand khĂ¡c nhau cĂ³ thá»ƒ cĂ¹ng cĂ³ CarModel "vios".
            b.HasIndex(x => new { x.BrandId, x.Slug }).IsUnique().HasDatabaseName(CatalogConstraints.CarModelBrandSlug);

            // Danh sĂ¡ch cĂ´ng khai: chá»‰ Published, má»›i nháº¥t trÆ°á»›c, hĂ²a thĂ¬ Slug (partial index nhá», Ä‘Ăºng truy váº¥n nĂ³ng).
            b.HasIndex(x => new { x.Status, x.PublishedAt, x.Slug })
                .IsDescending(false, true, false)
                .HasFilter("status = 'Published'")
                .HasDatabaseName("ix_cat_car_models_published");

            // Sáº¯p xáº¿p/lá»c theo giĂ¡ tháº¥p nháº¥t.
            b.HasIndex(x => new { x.MinPublishedPrice, x.Slug }).HasDatabaseName("ix_cat_car_models_min_price");

            // UNIQUE (brand_id, lower(name)) vĂ  GIN (search_text gin_trgm_ops) lĂ  SQL thĂ´ trong migration.
        });
    }

    private static void ConfigureCarVariant(ModelBuilder builder)
    {
        builder.Entity<CarVariant>(b =>
        {
            b.ToTable(TablePrefix + "car_variants", t =>
            {
                // ÄĂºng má»™t trong hai: giĂ¡ há»£p lá»‡ hoáº·c giĂ¡ liĂªn há»‡ (Req 8.4â€“8.6).
                t.HasCheckConstraint(
                    "ck_cat_car_variants_price",
                    "(contact_for_price AND reference_price IS NULL) OR (NOT contact_for_price AND reference_price BETWEEN 1 AND 100000000000)");
                t.HasCheckConstraint("ck_cat_car_variants_seats", "seats BETWEEN 2 AND 16");
                t.HasCheckConstraint("ck_cat_car_variants_status", "status IN ('Draft', 'Published', 'Hidden', 'Archived')");
            });

            b.ConfigureByConvention();

            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.CarModelId).HasColumnName("car_model_id").IsRequired();
            b.Property(x => x.Name).HasColumnName("name").HasColumnType("text").IsRequired();
            b.Property(x => x.Status).HasColumnName("status").HasColumnType("text").HasConversion<string>().IsRequired();
            b.Property(x => x.ReferencePrice).HasColumnName("reference_price").HasColumnType("bigint");
            b.Property(x => x.ContactForPrice).HasColumnName("contact_for_price").IsRequired();
            b.Property(x => x.BodyType).HasColumnName("body_type").HasColumnType("text").IsRequired();
            b.Property(x => x.Fuel).HasColumnName("fuel").HasColumnType("text").IsRequired();
            b.Property(x => x.Transmission).HasColumnName("transmission").HasColumnType("text").IsRequired();
            b.Property(x => x.Seats).HasColumnName("seats").HasColumnType("smallint").IsRequired();
            b.Property(x => x.Engine).HasColumnName("engine").HasColumnType("text");
            b.Property(x => x.Power).HasColumnName("power").HasColumnType("text");
            b.Property(x => x.IsDemo).HasColumnName("is_demo").HasDefaultValue(false).IsRequired();
            b.Property(x => x.ConcurrencyStamp).HasColumnName("concurrency_stamp");

            // Extra_Spec: máº£ng JSON trong jsonb. Danh sĂ¡ch bá»‹ THAY NGUYĂN khá»‘i khi sá»­a (record báº¥t biáº¿n), nhÆ°ng EF cáº§n
            // ValueComparer theo ná»™i dung Ä‘á»ƒ biáº¿t khi nĂ o "Ä‘Ă£ Ä‘á»•i"; converter tá»± viáº¿t mĂ  thiáº¿u comparer thĂ¬ sá»­a khĂ´ng Ä‘Æ°á»£c lÆ°u.
            b.Property(x => x.ExtraSpecs)
                .HasColumnName("extra_specs")
                .HasColumnType("jsonb")
                .IsRequired()
                .HasConversion(
                    v => ExtraSpecJson.Write(v),
                    v => ExtraSpecJson.Read(v),
                    new ValueComparer<IReadOnlyList<ExtraSpec>>(
                        (a, c) => ExtraSpecJson.SequenceEqual(a, c),
                        a => ExtraSpecJson.Hash(a),
                        a => ExtraSpecJson.Copy(a)));

            b.Ignore(x => x.KeySpec);

            b.HasIndex(x => new { x.CarModelId, x.Status }).HasDatabaseName("ix_cat_car_variants_model_status");

            // UNIQUE (car_model_id, lower(name)) lĂ  SQL thĂ´ trong migration (ux_cat_car_variants_model_name).
        });
    }

    private static void ConfigureVehicleImage(ModelBuilder builder)
    {
        builder.Entity<VehicleImage>(b =>
        {
            b.ToTable(TablePrefix + "vehicle_images");

            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.CarModelId).HasColumnName("car_model_id").IsRequired();
            b.Property(x => x.CarVariantId).HasColumnName("car_variant_id");
            b.Property(x => x.StorageKey).HasColumnName("storage_key").HasColumnType("text").IsRequired();
            b.Property(x => x.Alt).HasColumnName("alt").HasColumnType("text").IsRequired();
            b.Property(x => x.DisplayOrder).HasColumnName("display_order").IsRequired();
            b.Property(x => x.IsCover).HasColumnName("is_cover").IsRequired();

            // áº¢nh cĂ³ thá»ƒ gáº¯n vá»›i má»™t variant (tĂ¹y chá»n); xĂ³a variant thĂ¬ áº£nh váº«n cĂ²n, chá»‰ máº¥t liĂªn káº¿t.
            b.HasOne<CarVariant>().WithMany().HasForeignKey(x => x.CarVariantId).OnDelete(DeleteBehavior.SetNull);

            // Má»i áº£nh cá»§a má»™t model (index cá»§a FK; index cover bĂªn dÆ°á»›i chá»‰ chá»©a cĂ¡c dĂ²ng is_cover).
            b.HasIndex(x => new { x.CarModelId, x.DisplayOrder }).HasDatabaseName("ix_cat_vehicle_images_model_order");

            // Tá»‘i Ä‘a Má»˜T áº£nh cover má»—i CarModel, do DB báº£o Ä‘áº£m (Req 9.7).
            b.HasIndex(x => x.CarModelId).IsUnique().HasFilter("is_cover").HasDatabaseName("ux_cat_vehicle_images_cover");
        });
    }

    private static void ConfigureSlugHistory(ModelBuilder builder)
    {
        builder.Entity<SlugHistory>(b =>
        {
            b.ToTable(TablePrefix + "slug_history");

            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(x => x.EntityType).HasColumnName("entity_type").HasColumnType("text").IsRequired();
            b.Property(x => x.EntityId).HasColumnName("entity_id").IsRequired();
            b.Property(x => x.ScopeId).HasColumnName("scope_id").IsRequired();
            b.Property(x => x.OldSlug).HasColumnName("old_slug").HasColumnType("text").IsRequired();
            b.Property(x => x.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone").IsRequired()
                .HasConversion(v => v, v => v.ToUniversalTime());

            // scope_id luĂ´n cĂ³ giĂ¡ trá»‹ (UUID zero cho Brand, khĂ´ng NULL) Ä‘á»ƒ UNIQUE cháº·n trĂ¹ng Ä‘Ăºng nghÄ©a.
            b.HasIndex(x => new { x.EntityType, x.ScopeId, x.OldSlug }).IsUnique().HasDatabaseName(CatalogConstraints.SlugHistoryOldSlug);
            b.HasIndex(x => new { x.EntityType, x.EntityId }).HasDatabaseName("ix_cat_slug_history_entity");
        });
    }

    /// <summary>
    /// <see cref="ConfigureAbpEntityExtensions.ConfigureByConvention"/> Ä‘áº·t cá»™t theo PascalCase cá»§a ABP.
    /// Äá»•i sang snake_case cho báº£ng cá»§a dá»± Ă¡n vĂ  Ă©p má»i thá»i Ä‘iá»ƒm vá» UTC khi Ä‘á»c (switch legacy cá»§a Npgsql tráº£ Kind=Local).
    /// </summary>
    internal static void ConfigureAuditedColumns<T>(this EntityTypeBuilder<T> b) where T : AuditedAggregateRoot<Guid>
    {
        b.Property(x => x.ExtraProperties).HasColumnName("extra_properties");
        b.Property(x => x.ConcurrencyStamp).HasColumnName("concurrency_stamp");
        b.Property(x => x.CreatorId).HasColumnName("creator_id");
        b.Property(x => x.LastModifierId).HasColumnName("last_modifier_id");
        b.Property(x => x.CreationTime).HasColumnName("creation_time").HasColumnType("timestamp with time zone")
            .HasConversion(v => v, v => v.ToUniversalTime());
        b.Property(x => x.LastModificationTime).HasColumnName("last_modification_time").HasColumnType("timestamp with time zone")
            .HasConversion(v => v, v => v.HasValue ? v.Value.ToUniversalTime() : v);
    }
}

/// <summary>
/// TĂªn rĂ ng buá»™c/index cá»§a Catalog. DĂ¹ng cho mapping, migration SQL thĂ´ vĂ  Ă¡nh xáº¡ vi pháº¡m UNIQUE (SQLSTATE 23505)
/// sang lá»—i 409 theo trÆ°á»ng (<see cref="CatalogUniqueViolations"/>).
/// </summary>
public static class CatalogConstraints
{
    public const string BrandName = "ux_cat_brands_name";
    public const string BrandSlug = "ux_cat_brands_slug";
    public const string CarModelBrandName = "ux_cat_car_models_brand_name";
    public const string CarModelBrandSlug = "ux_cat_car_models_brand_slug";
    public const string CarVariantModelName = "ux_cat_car_variants_model_name";
    public const string SlugHistoryOldSlug = "ux_cat_slug_history_old_slug";
}
