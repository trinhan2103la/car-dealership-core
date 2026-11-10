using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Catalog;

/// <summary>Bảng/ràng buộc/index Catalog sau migration thật (thay cho kiểm tay bằng psql \d).</summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class CatalogSchemaTests : CatalogTestBase
{
    private Task<string[]> IndexesAsync(string table) =>
        WithDbAsync(async db => (await db.Database
                .SqlQuery<string>($"select indexdef as \"Value\" from pg_indexes where tablename = {table}")
                .ToListAsync())
            .ToArray());

    private Task<string[]> ChecksAsync(string table) =>
        WithDbAsync(async db => (await db.Database
                .SqlQuery<string>($"select conname as \"Value\" from pg_constraint where contype = 'c' and conrelid = {table}::regclass")
                .ToListAsync())
            .ToArray());

    [Fact]
    public async Task Brands_have_unique_lower_name_unique_slug_and_slug_check()
    {
        var indexes = await IndexesAsync("cat_brands");
        indexes.ShouldContain(i => i.Contains("ux_cat_brands_name") && i.Contains("UNIQUE") && i.Contains("lower(name)"));
        indexes.ShouldContain(i => i.Contains("ux_cat_brands_slug") && i.Contains("UNIQUE") && i.Contains("(slug)"));
        (await ChecksAsync("cat_brands")).ShouldContain("ck_cat_brands_slug");
    }

    [Fact]
    public async Task Car_models_have_per_brand_uniques_published_partial_index_price_index_and_gin_trgm()
    {
        var indexes = await IndexesAsync("cat_car_models");
        indexes.ShouldContain(i => i.Contains("ux_cat_car_models_brand_slug") && i.Contains("UNIQUE") && i.Contains("(brand_id, slug)"));
        indexes.ShouldContain(i => i.Contains("ux_cat_car_models_brand_name") && i.Contains("UNIQUE") && i.Contains("lower(name)"));
        indexes.ShouldContain(i => i.Contains("ix_cat_car_models_published") && i.Contains("published_at DESC") && i.Contains("WHERE"));
        indexes.ShouldContain(i => i.Contains("ix_cat_car_models_min_price") && i.Contains("(min_published_price, slug)"));
        indexes.ShouldContain(i => i.Contains("ix_cat_car_models_search_text") && i.Contains("gin") && i.Contains("gin_trgm_ops"));
        (await ChecksAsync("cat_car_models")).ShouldContain("ck_cat_car_models_display_order");
    }

    [Fact]
    public async Task Car_variants_have_price_and_seats_checks_and_unique_lower_name_per_model()
    {
        (await ChecksAsync("cat_car_variants")).ShouldContain("ck_cat_car_variants_price");
        (await ChecksAsync("cat_car_variants")).ShouldContain("ck_cat_car_variants_seats");
        (await IndexesAsync("cat_car_variants"))
            .ShouldContain(i => i.Contains("ux_cat_car_variants_model_name") && i.Contains("UNIQUE") && i.Contains("lower(name)"));

        var extraSpecsType = await WithDbAsync(db => db.Database
            .SqlQuery<string>($"select data_type as \"Value\" from information_schema.columns where table_name = 'cat_car_variants' and column_name = 'extra_specs'")
            .SingleAsync());
        extraSpecsType.ShouldBe("jsonb");
    }

    [Fact]
    public async Task Vehicle_images_allow_at_most_one_cover_per_model()
    {
        (await IndexesAsync("cat_vehicle_images"))
            .ShouldContain(i => i.Contains("ux_cat_vehicle_images_cover") && i.Contains("UNIQUE") && i.Contains("WHERE is_cover"));
    }

    [Fact]
    public async Task Slug_history_is_unique_on_entity_type_scope_and_old_slug()
    {
        (await IndexesAsync("cat_slug_history"))
            .ShouldContain(i => i.Contains("ux_cat_slug_history_old_slug") && i.Contains("(entity_type, scope_id, old_slug)"));
    }

    [Fact]
    public async Task Pg_trgm_extension_is_installed()
    {
        var extensions = await WithDbAsync(db => db.Database
            .SqlQuery<string>($"select extname as \"Value\" from pg_extension")
            .ToListAsync());
        extensions.ShouldContain("pg_trgm");
    }

    [Fact]
    public async Task Database_rejects_a_variant_with_both_price_and_contact_flag()
    {
        var brand = await CreateBrandAsync();
        var model = await AsAdminAsync(() => GetRequiredService<CarModels.ICarModelAppService>().CreateAsync(new CarModels.CreateCarModelInput
        {
            BrandId = brand.Id, Name = "Vios", Slug = "vios",
        }));

        var ex = await Should.ThrowAsync<Npgsql.PostgresException>(() => WithDbAsync(async db =>
        {
            return await db.Database.ExecuteSqlAsync($@"
                insert into cat_car_variants (id, car_model_id, name, status, reference_price, contact_for_price,
                    body_type, fuel, transmission, seats, extra_specs, is_demo, concurrency_stamp)
                values ({System.Guid.NewGuid()}, {model.Id}, 'x', 'Draft', 100, true, 'Sedan', 'Xăng', 'AT', 5, '[]'::jsonb, false, 'x')");
        }));

        ex.SqlState.ShouldBe("23514"); // check_violation
        ex.ConstraintName.ShouldBe("ck_cat_car_variants_price");
    }
}
