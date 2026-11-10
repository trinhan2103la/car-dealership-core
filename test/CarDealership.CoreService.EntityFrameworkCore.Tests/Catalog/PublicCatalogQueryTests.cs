using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Public;
using Shouldly;
using Volo.Abp.Validation;
using Xunit;

namespace CarDealership.CoreService.Catalog;

/// <summary>Truy vấn danh mục công khai trên DB thật: lọc AND/OR, giá theo từng variant, sắp xếp + Slug, phân trang (Req 2).</summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class PublicCatalogQueryTests : CatalogTestBase
{
    private IPublicCatalogAppService Public => GetRequiredService<IPublicCatalogAppService>();

    private Task<PublicCarListDto> SearchAsync(PublicCarModelListInput input) =>
        AsAdminAsync(() => Public.GetCarModelsAsync(input));

    private static List<string> Slugs(PublicCarListDto page) => page.Items.Select(i => i.Slug).ToList();

    /// <summary>Danh mục mẫu: 2 Brand, 4 model Published, 1 model Draft, 1 model Published nhưng không còn variant nào phù hợp.</summary>
    private async Task<(BrandDto Toyota, BrandDto Honda)> SeedAsync()
    {
        var toyota = await CreateBrandAsync("Toyota", "toyota");
        var honda = await CreateBrandAsync("Honda", "honda");

        await CreatePublishedModelAsync("Vios", toyota, VariantInput("1.5G", 458_000_000m, fuel: "Xăng", transmission: "Số tự động"));
        var (_, camry) = await CreatePublishedModelAsync("Camry", toyota, VariantInput("2.0", 1_100_000_000m, fuel: "Xăng"));
        await AddPublishedVariantAsync(camry.Id, VariantInput("2.5 Hybrid", 1_500_000_000m, fuel: "Hybrid"));
        await CreatePublishedModelAsync("City", honda, VariantInput("RS", 600_000_000m, fuel: "Xăng", transmission: "Số sàn"));
        await CreatePublishedModelAsync("CR-V", honda, VariantInput("L", contact: true, bodyType: "SUV", fuel: "Dầu", seats: 7));
        await CreateModelAsync(toyota.Id, "Nháp Draft");
        return (toyota, honda);
    }

    [Fact]
    public async Task Only_published_models_with_a_published_variant_are_listed_newest_first()
    {
        await SeedAsync();

        var page = await SearchAsync(new PublicCarModelListInput());

        Slugs(page).ShouldBe(new[] { "cr-v", "city", "camry", "vios" });
        page.TotalItems.ShouldBe(4);
        page.TotalPages.ShouldBe(1);
        page.PageSize.ShouldBe(12);
    }

    [Fact]
    public async Task Card_has_brand_price_range_cover_and_key_specs()
    {
        await SeedAsync();

        var camry = (await SearchAsync(new PublicCarModelListInput { Q = "camry" })).Items.Single();

        camry.BrandName.ShouldBe("Toyota");
        camry.MinPrice.ShouldBe(1_100_000_000);
        camry.MaxPrice.ShouldBe(1_500_000_000);
        camry.CoverStorageKey.ShouldNotBeNull();
        camry.Fuels.ShouldBe(new[] { "Hybrid", "Xăng" });
        camry.Seats.ShouldBe(new[] { 5 });
    }

    [Fact]
    public async Task Keyword_is_accent_and_case_insensitive_and_matches_variant_names()
    {
        await SeedAsync();

        Slugs(await SearchAsync(new PublicCarModelListInput { Q = "TOYOTA" })).ShouldBe(new[] { "camry", "vios" }, ignoreOrder: true);
        Slugs(await SearchAsync(new PublicCarModelListInput { Q = "hybrid" })).ShouldBe(new[] { "camry" });
        Slugs(await SearchAsync(new PublicCarModelListInput { Q = "  " })).Count.ShouldBe(4);
        Slugs(await SearchAsync(new PublicCarModelListInput { Q = "nháp" })).ShouldBeEmpty();   // Draft không bao giờ lộ
    }

    [Fact]
    public async Task Percent_and_underscore_in_the_keyword_are_literal()
    {
        await SeedAsync();

        (await SearchAsync(new PublicCarModelListInput { Q = "%" })).TotalItems.ShouldBe(0);
        (await SearchAsync(new PublicCarModelListInput { Q = "_" })).TotalItems.ShouldBe(0);
    }

    [Fact]
    public async Task Filters_are_or_within_a_criterion_and_and_across_criteria()
    {
        await SeedAsync();

        Slugs(await SearchAsync(new PublicCarModelListInput { Brand = new() { "honda" } }))
            .ShouldBe(new[] { "cr-v", "city" });
        Slugs(await SearchAsync(new PublicCarModelListInput { Fuel = new() { "Dầu", "Hybrid" } }))
            .ShouldBe(new[] { "cr-v", "camry" });
        Slugs(await SearchAsync(new PublicCarModelListInput { Brand = new() { "toyota" }, Fuel = new() { "Dầu" } }))
            .ShouldBeEmpty();
        Slugs(await SearchAsync(new PublicCarModelListInput { Seats = new() { 7 }, BodyType = new() { "SUV" } }))
            .ShouldBe(new[] { "cr-v" });
    }

    [Fact]
    public async Task Variant_criteria_must_be_met_by_the_same_variant()
    {
        await SeedAsync();

        // Camry có variant Xăng 1,1 tỷ và variant Hybrid 1,5 tỷ: "Xăng" + giá ≥ 1,2 tỷ không do CÙNG một variant thỏa.
        Slugs(await SearchAsync(new PublicCarModelListInput { Fuel = new() { "Xăng" }, MinPrice = 1_200_000_000 }))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Price_range_is_inclusive_checked_per_variant_and_skips_contact_for_price()
    {
        await SeedAsync();

        Slugs(await SearchAsync(new PublicCarModelListInput { MinPrice = 458_000_000, MaxPrice = 600_000_000 }))
            .ShouldBe(new[] { "city", "vios" });
        // Khoảng nằm GIỮA hai variant của Camry (1,1 tỷ và 1,5 tỷ): không khớp dù min/max của model bao trùm.
        Slugs(await SearchAsync(new PublicCarModelListInput { MinPrice = 1_200_000_000, MaxPrice = 1_400_000_000 }))
            .ShouldBeEmpty();
        Slugs(await SearchAsync(new PublicCarModelListInput { MinPrice = 0 })).ShouldNotContain("cr-v");
    }

    [Fact]
    public async Task Sorting_by_price_and_name_with_slug_as_tie_breaker()
    {
        await SeedAsync();

        Slugs(await SearchAsync(new PublicCarModelListInput { Sort = "price_asc" }))
            .ShouldBe(new[] { "vios", "city", "camry", "cr-v" });   // giá liên hệ (null) cuối
        Slugs(await SearchAsync(new PublicCarModelListInput { Sort = "price_desc" }))
            .ShouldBe(new[] { "camry", "city", "vios", "cr-v" });
        Slugs(await SearchAsync(new PublicCarModelListInput { Sort = "name_asc" }))
            .ShouldBe(new[] { "city", "cr-v", "camry", "vios" });
    }

    [Fact]
    public async Task Page_beyond_the_last_returns_no_items_but_the_total()
    {
        await SeedAsync();

        var page = await SearchAsync(new PublicCarModelListInput { Page = 3, PageSize = 2 });

        page.Items.ShouldBeEmpty();
        page.TotalItems.ShouldBe(4);
        page.TotalPages.ShouldBe(2);
        Slugs(await SearchAsync(new PublicCarModelListInput { Page = 2, PageSize = 2 })).ShouldBe(new[] { "camry", "vios" });
    }

    [Fact]
    public async Task Invalid_parameters_are_all_reported_by_name()
    {
        await SeedAsync();

        var ex = await Should.ThrowAsync<AbpValidationException>(() => SearchAsync(new PublicCarModelListInput
        {
            Page = 0,
            PageSize = 49,
            Sort = "random",
            Q = new string('q', 101),
            MinPrice = 10,
            MaxPrice = 5,
            Brand = new() { "khong-ton-tai" },
            Seats = new() { 1 },
        }));

        ex.ValidationErrors.SelectMany(e => e.MemberNames).Distinct()
            .ShouldBe(new[] { "page", "pageSize", "sort", "q", "minPrice", "brand", "seats" }, ignoreOrder: true);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(48, true)]
    [InlineData(0, false)]
    [InlineData(49, false)]
    public async Task Page_size_boundaries(int pageSize, bool valid)
    {
        var act = () => SearchAsync(new PublicCarModelListInput { PageSize = pageSize });

        if (valid)
        {
            (await act()).PageSize.ShouldBe(pageSize);
        }
        else
        {
            await Should.ThrowAsync<AbpValidationException>(act);
        }
    }

    [Fact]
    public async Task Keyword_of_100_characters_is_accepted()
    {
        (await SearchAsync(new PublicCarModelListInput { Q = new string('q', 100) })).TotalItems.ShouldBe(0);
    }

    // ---------------------------------------------------------------- trang chủ, Brand, sitemap

    [Fact]
    public async Task Home_cars_brands_and_sitemap_only_expose_public_data()
    {
        var toyota = await CreateBrandAsync("Toyota", "toyota");
        await CreateBrandAsync("Inactive", "inactive", BrandStatus.Inactive);
        await CreatePublishedModelAsync("Vios", toyota, featured: true);
        await CreatePublishedModelAsync("Camry", toyota);
        await CreateModelAsync(toyota.Id, "Nháp", featured: true);

        var home = await AsAdminAsync(() => Public.GetHomeCarsAsync());
        home.Select(c => c.Slug).ShouldBe(new[] { "vios" });

        var brands = await AsAdminAsync(() => Public.GetBrandsAsync());
        brands.Select(b => b.Slug).ShouldBe(new[] { "toyota" });

        var sitemap = await AsAdminAsync(() => Public.GetSitemapAsync());
        sitemap.Select(s => s.Path).ShouldBe(new[] { "/xe/toyota/camry", "/xe/toyota/vios" });
    }
}
