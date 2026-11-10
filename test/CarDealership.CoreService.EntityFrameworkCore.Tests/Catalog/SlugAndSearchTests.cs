using System;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Public;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace CarDealership.CoreService.Catalog;

/// <summary>Lịch sử Slug + resolver chuyển hướng một bước (Req 3.8, 3.9, 8.13) và search_text (Req 2.8).</summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class SlugAndSearchTests : CatalogTestBase
{
    private IPublicCatalogAppService Public => GetRequiredService<IPublicCatalogAppService>();

    private Task RenameModelSlugAsync(CarModelDto model, string newSlug) =>
        AsAdminAsync(() => CarModels.UpdateAsync(model.Id, new UpdateCarModelInput
        {
            Name = model.Name, Slug = newSlug, Description = model.Description ?? LongDescription,
        }));

    private Task RenameBrandSlugAsync(BrandDto brand, string newSlug) =>
        AsAdminAsync(() => Brands.UpdateAsync(brand.Id, new SaveBrandInput { Name = brand.Name, Slug = newSlug }));

    private Task<PublicCarModelLookupDto> LookupAsync(string brandSlug, string modelSlug) =>
        AsAdminAsync(() => Public.GetCarModelBySlugsAsync(brandSlug, modelSlug));

    [Fact]
    public async Task Current_slugs_return_the_model()
    {
        var brand = await CreateBrandAsync("Toyota", "toyota");
        var (_, model) = await CreatePublishedModelAsync("Vios", brand);

        var result = await LookupAsync("toyota", "vios");

        result.Redirect.ShouldBeNull();
        result.CarModel!.Id.ShouldBe(model.Id);
        result.CarModel.BrandName.ShouldBe("Toyota");
        result.CarModel.Variants.Count.ShouldBe(1);
        result.CarModel.Images.First().IsCover.ShouldBeTrue();
    }

    [Fact]
    public async Task Old_model_slug_old_brand_slug_and_both_redirect_in_one_step()
    {
        var brand = await CreateBrandAsync("Toyota", "toyota");
        var (_, model) = await CreatePublishedModelAsync("Vios", brand);
        await RenameModelSlugAsync(model, "vios-2026");
        await RenameBrandSlugAsync(brand, "toyota-vn");

        (await LookupAsync("toyota-vn", "vios")).Redirect.ShouldBe("/xe/toyota-vn/vios-2026");     // Brand mới + model cũ
        (await LookupAsync("toyota", "vios-2026")).Redirect.ShouldBe("/xe/toyota-vn/vios-2026");   // Brand cũ + model mới
        (await LookupAsync("toyota", "vios")).Redirect.ShouldBe("/xe/toyota-vn/vios-2026");        // cả hai cũ
        (await LookupAsync("toyota-vn", "vios-2026")).Redirect.ShouldBeNull();
    }

    [Fact]
    public async Task Renaming_twice_still_redirects_the_first_slug_straight_to_the_current_one()
    {
        var brand = await CreateBrandAsync("Mazda", "mazda");
        var (_, model) = await CreatePublishedModelAsync("CX 5", brand);
        await RenameModelSlugAsync(model, "cx5");
        var current = await AsAdminAsync(() => CarModels.GetAsync(model.Id));
        await RenameModelSlugAsync(current, "cx-5-2026");

        (await LookupAsync("mazda", "cx-5")).Redirect.ShouldBe("/xe/mazda/cx-5-2026");
        (await LookupAsync("mazda", "cx5")).Redirect.ShouldBe("/xe/mazda/cx-5-2026");
    }

    [Fact]
    public async Task Same_model_slug_under_two_brands_does_not_collide_in_history()
    {
        var toyota = await CreateBrandAsync("Toyota", "toyota");
        var honda = await CreateBrandAsync("Honda", "honda");
        var (_, t) = await CreatePublishedModelAsync("Vios", toyota);
        var (_, h) = await CreatePublishedModelAsync("Vios", honda);

        await RenameModelSlugAsync(t, "vios-t");
        await RenameModelSlugAsync(h, "vios-h");

        (await LookupAsync("toyota", "vios")).Redirect.ShouldBe("/xe/toyota/vios-t");
        (await LookupAsync("honda", "vios")).Redirect.ShouldBe("/xe/honda/vios-h");
    }

    [Fact]
    public async Task Another_model_in_the_same_brand_cannot_take_an_old_slug()
    {
        var brand = await CreateBrandAsync();
        var (_, vios) = await CreatePublishedModelAsync("Vios", brand);
        await RenameModelSlugAsync(vios, "vios-moi");
        var other = await CreateModelAsync(brand.Id, "Khác");

        var ex = await Should.ThrowAsync<BusinessException>(() => RenameModelSlugAsync(other, "vios"));

        ex.Code.ShouldBe(CatalogErrorCodes.Duplicate);
    }

    [Fact]
    public async Task Slug_of_a_never_published_model_is_not_kept_in_history()
    {
        var brand = await CreateBrandAsync();
        var draft = await CreateModelAsync(brand.Id, "Nháp");

        await RenameModelSlugAsync(draft, "nhap-2");

        (await WithDbAsync(db => db.SlugHistories.CountAsync(h => h.EntityType == CatalogEntityTypes.CarModel))).ShouldBe(0);
    }

    [Fact]
    public async Task Non_published_unknown_or_mismatched_brand_is_404()
    {
        var toyota = await CreateBrandAsync("Toyota", "toyota");
        var honda = await CreateBrandAsync("Honda", "honda");
        var draft = await CreateModelAsync(toyota.Id, "Nháp");
        var (_, published) = await CreatePublishedModelAsync("Vios", toyota);

        await Should.ThrowAsync<EntityNotFoundException>(() => LookupAsync("toyota", draft.Slug));
        await Should.ThrowAsync<EntityNotFoundException>(() => LookupAsync("toyota", "khong-co"));
        await Should.ThrowAsync<EntityNotFoundException>(() => LookupAsync("honda", published.Slug));   // brand không sở hữu
        await Should.ThrowAsync<EntityNotFoundException>(() => LookupAsync("--", "vios"));

        await AsAdminAsync(() => CarModels.ChangeStatusAsync(published.Id, new ChangeCatalogStatusInput { Status = CatalogStatus.Hidden }));
        await Should.ThrowAsync<EntityNotFoundException>(() => LookupAsync("toyota", "vios"));
    }

    // ---------------------------------------------------------------- search_text

    private Task<string> SearchTextAsync(Guid modelId) =>
        WithDbAsync(db => db.CarModels.AsNoTracking().Where(m => m.Id == modelId).Select(m => m.SearchText).SingleAsync());

    [Fact]
    public async Task Search_text_follows_variant_publishing_and_brand_renames()
    {
        var brand = await CreateBrandAsync("Đông Phong", "dong-phong");
        var model = await CreateModelAsync(brand.Id, "Xe Điện");

        (await SearchTextAsync(model.Id)).ShouldBe("dong phong xe dien");

        await AddPublishedVariantAsync(model.Id, VariantInput("Bản Đặc Biệt"));
        (await SearchTextAsync(model.Id)).ShouldBe("dong phong xe dien ban dac biet");

        await AsAdminAsync(() => Brands.UpdateAsync(brand.Id, new SaveBrandInput { Name = "Đông Phong Mới", Slug = "dong-phong" }));
        (await SearchTextAsync(model.Id)).ShouldBe("dong phong moi xe dien ban dac biet");
    }
}
