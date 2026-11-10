using System;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.Images;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace CarDealership.CoreService.Catalog;

/// <summary>Giới hạn 30 ảnh có khóa hàng (kể cả đồng thời) và tối đa một cover do DB bảo đảm (Req 9.5, 9.7).</summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class VehicleImageLimitTests : CatalogTestBase
{
    private Task<int> ImageCountAsync(Guid modelId) =>
        WithDbAsync(db => db.Database
            .SqlQuery<int>($"select count(*)::int as \"Value\" from cat_vehicle_images where car_model_id = {modelId}")
            .SingleAsync());

    [Fact]
    public async Task Two_concurrent_uploads_when_29_images_exist_let_exactly_one_through()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        for (var i = 0; i < 29; i++)
        {
            await AddImageAsync(model.Id);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try
            {
                await AddImageAsync(model.Id);
                return null;
            }
            catch (BusinessException ex)
            {
                return ex.Code;
            }
        }));

        results.Count(r => r is null).ShouldBe(1);
        results.Single(r => r is not null).ShouldBe(CatalogErrorCodes.ImageLimitReached);
        (await ImageCountAsync(model.Id)).ShouldBe(30);
    }

    [Fact]
    public async Task Display_order_is_contiguous_and_storage_keys_are_random()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        await AddImageAsync(model.Id);
        await AddImageAsync(model.Id);
        await AddImageAsync(model.Id);

        var images = (await AsAdminAsync(() => CarModels.GetAsync(model.Id))).Images;

        images.Select(i => i.DisplayOrder).ShouldBe(new[] { 0, 1, 2 });
        images.Select(i => i.StorageKey).Distinct().Count().ShouldBe(3);
        foreach (var image in images)
        {
            Guid.TryParseExact(image.StorageKey, "N", out var _).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Changing_the_cover_keeps_exactly_one_cover()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        var first = await AddImageAsync(model.Id, cover: true);
        var second = await AddImageAsync(model.Id);

        await AsAdminAsync(() => GetRequiredService<VehicleImageManager>().SetCoverAsync(model.Id, second));

        var images = (await AsAdminAsync(() => CarModels.GetAsync(model.Id))).Images;
        images.Count(i => i.IsCover).ShouldBe(1);
        images.Single(i => i.IsCover).Id.ShouldBe(second);
        images.Single(i => i.Id == first).IsCover.ShouldBeFalse();
    }

    [Fact]
    public async Task Database_rejects_a_second_cover_written_behind_the_aggregate()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        await AddImageAsync(model.Id, cover: true);
        var second = await AddImageAsync(model.Id);

        var ex = await Should.ThrowAsync<Npgsql.PostgresException>(() => WithDbAsync(db =>
            db.Database.ExecuteSqlAsync($"update cat_vehicle_images set is_cover = true where id = {second}")));

        ex.SqlState.ShouldBe("23505");
    }

    [Fact]
    public async Task Archived_model_takes_no_new_image()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        await AsAdminAsync(() => CarModels.ChangeStatusAsync(model.Id, new CarModels.ChangeCatalogStatusInput { Status = CatalogStatus.Archived }));

        var ex = await Should.ThrowAsync<BusinessException>(() => AddImageAsync(model.Id));

        ex.Code.ShouldBe(CatalogErrorCodes.CarModelArchived);
        (await ImageCountAsync(model.Id)).ShouldBe(0);
    }
}
