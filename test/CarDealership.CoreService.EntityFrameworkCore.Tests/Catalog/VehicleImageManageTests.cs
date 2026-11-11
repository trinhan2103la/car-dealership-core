using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Audit;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Images;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Validation;
using Xunit;

namespace CarDealership.CoreService.Catalog;

/// <summary>Sắp xếp, cover, alt và xóa ảnh (Req 9.6, 9.7, 9.9, 10.10). Xóa object chỉ sau commit; storage lỗi thì vào job.</summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class VehicleImageManageTests : CatalogTestBase, IClassFixture<S3MockFixture>
{
    private readonly S3MockFixture _s3;

    public VehicleImageManageTests(S3MockFixture s3)
    {
        _s3 = s3;
    }

    private IVehicleImageAppService Images => GetRequiredService<IVehicleImageAppService>();

    private S3ImageStorage UseStorage(string? serviceUrl = null)
    {
        var storage = new S3ImageStorage(Options.Create(new ImageStorageOptions
        {
            ServiceUrl = serviceUrl ?? _s3.ServiceUrl,
            Bucket = S3MockFixture.Bucket,
            AccessKey = S3MockFixture.AccessKey,
            SecretKey = S3MockFixture.SecretKey,
            TimeoutSeconds = 3,
        }));
        GetRequiredService<ImageStorageSwitch>().Use(storage);
        return storage;
    }

    private static IRemoteStreamContent WebP()
    {
        var bytes = new byte[32];
        "RIFF"u8.CopyTo(bytes);
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        return new RemoteStreamContent(new MemoryStream(bytes), "image.webp");
    }

    private Task<VehicleImageDto> UploadAsync(Guid modelId, string alt = "Ảnh xe đẹp") =>
        AsAdminAsync(() => Images.UploadAsync(modelId, new UploadVehicleImageInput { Sm = WebP(), Md = WebP(), Lg = WebP(), Alt = alt }));

    private async Task<System.Collections.Generic.List<VehicleImageDto>> ImagesOfAsync(Guid modelId) =>
        (await AsAdminAsync(() => CarModels.GetAsync(modelId))).Images.OrderBy(i => i.DisplayOrder).ToList();

    private Task<int> BackgroundJobCountAsync() =>
        WithDbAsync(db => db.Database
            .SqlQueryRaw<int>("select count(*)::int as \"Value\" from \"AbpBackgroundJobs\"")
            .SingleAsync());

    // ---------------------------------------------------------------- reorder + cover

    [Fact]
    public async Task Reorder_sets_contiguous_order_and_moves_the_cover()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        var a = await AddImageAsync(model.Id, cover: true);
        var b = await AddImageAsync(model.Id);
        var c = await AddImageAsync(model.Id);

        var result = await AsAdminAsync(() => Images.ReorderAsync(model.Id,
            new ReorderImagesInput { ImageIds = { c, a, b }, CoverImageId = b }));

        result.Select(i => i.Id).ShouldBe(new[] { c, a, b });
        var stored = await ImagesOfAsync(model.Id);
        stored.Select(i => i.Id).ShouldBe(new[] { c, a, b });
        stored.Select(i => i.DisplayOrder).ShouldBe(new[] { 0, 1, 2 });
        stored.Count(i => i.IsCover).ShouldBe(1);
        stored.Single(i => i.IsCover).Id.ShouldBe(b);

        var audits = (await AuditEntriesAsync(model.Id.ToString()))
            .Where(e => e.EntityType == CatalogEntityTypes.CarModel && e.Action == AuditActions.Update)
            .ToList();
        audits.Count.ShouldBe(1);
        audits[0].Changes.ShouldContain("images");
    }

    [Fact]
    public async Task Reorder_with_a_missing_image_is_400_and_changes_nothing()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        var a = await AddImageAsync(model.Id);
        var b = await AddImageAsync(model.Id);
        await AddImageAsync(model.Id);

        var ex = await Should.ThrowAsync<AbpValidationException>(() => AsAdminAsync(() =>
            Images.ReorderAsync(model.Id, new ReorderImagesInput { ImageIds = { b, a } })));

        ex.ValidationErrors.SelectMany(e => e.MemberNames).ShouldBe(new[] { "imageIds" });
        (await ImagesOfAsync(model.Id)).Take(2).Select(i => i.Id).ShouldBe(new[] { a, b });
    }

    [Fact]
    public async Task Cover_that_is_not_an_image_of_the_model_is_400()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        var a = await AddImageAsync(model.Id);

        var ex = await Should.ThrowAsync<AbpValidationException>(() => AsAdminAsync(() =>
            Images.ReorderAsync(model.Id, new ReorderImagesInput { ImageIds = { a }, CoverImageId = Guid.NewGuid() })));

        ex.ValidationErrors.SelectMany(e => e.MemberNames).ShouldBe(new[] { "coverImageId" });
    }

    // ---------------------------------------------------------------- alt

    [Fact]
    public async Task Update_alt_writes_one_audit_entry()
    {
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        var a = await AddImageAsync(model.Id);

        var dto = await AsAdminAsync(() => Images.UpdateAsync(model.Id, a, new UpdateImageInput { Alt = "  Nội thất xe  " }));

        dto.Alt.ShouldBe("Nội thất xe");
        var audits = (await AuditEntriesAsync(a.ToString()))
            .Where(e => e.EntityType == CatalogEntityTypes.VehicleImage && e.Action == AuditActions.Update)
            .ToList();
        audits.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Short_alt_on_a_published_model_is_422_and_keeps_the_old_alt()
    {
        var (_, model) = await CreatePublishedModelAsync();
        var cover = model.Images.Single(i => i.IsCover);

        var ex = await Should.ThrowAsync<BusinessException>(() => AsAdminAsync(() =>
            Images.UpdateAsync(model.Id, cover.Id, new UpdateImageInput { Alt = "abc" })));

        ex.Code.ShouldBe(CatalogErrorCodes.PublishValidationFailed);
        (await ImagesOfAsync(model.Id)).Single(i => i.Id == cover.Id).Alt.ShouldBe(cover.Alt);
    }

    // ---------------------------------------------------------------- delete

    [Fact]
    public async Task Delete_removes_the_record_then_the_three_objects_and_reorders()
    {
        var storage = UseStorage();
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        var first = await UploadAsync(model.Id);
        var second = await UploadAsync(model.Id);
        var third = await UploadAsync(model.Id);

        await AsAdminAsync(() => Images.DeleteAsync(model.Id, second.Id));

        foreach (var size in ImageStorageKeys.Sizes)
        {
            (await storage.ExistsAsync(ImageStorageKeys.ObjectKey(model.Id, second.StorageKey, size))).ShouldBeFalse();
            (await storage.ExistsAsync(ImageStorageKeys.ObjectKey(model.Id, first.StorageKey, size))).ShouldBeTrue();
        }

        var remaining = await ImagesOfAsync(model.Id);
        remaining.Select(i => i.Id).ShouldBe(new[] { first.Id, third.Id });
        remaining.Select(i => i.DisplayOrder).ShouldBe(new[] { 0, 1 });

        var audits = (await AuditEntriesAsync(second.Id.ToString()))
            .Where(e => e.Action == AuditActions.Delete)
            .ToList();
        audits.Count.ShouldBe(1);
        (await BackgroundJobCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Delete_that_fails_validation_keeps_the_objects()
    {
        var storage = UseStorage();
        var (_, published) = await CreatePublishedModelAsync();
        var uploaded = await UploadAsync(published.Id);
        // Đổi cover sang ảnh vừa upload, rồi thử xóa nó: model Published mất cover → 422, rollback.
        await AsAdminAsync(() => Images.ReorderAsync(published.Id, new ReorderImagesInput
        {
            ImageIds = (published.Images.Select(i => i.Id).Append(uploaded.Id)).ToList(),
            CoverImageId = uploaded.Id,
        }));

        var ex = await Should.ThrowAsync<BusinessException>(() => AsAdminAsync(() => Images.DeleteAsync(published.Id, uploaded.Id)));

        ex.Code.ShouldBe(CatalogErrorCodes.PublishValidationFailed);
        foreach (var size in ImageStorageKeys.Sizes)
        {
            (await storage.ExistsAsync(ImageStorageKeys.ObjectKey(published.Id, uploaded.StorageKey, size))).ShouldBeTrue();
        }

        (await ImagesOfAsync(published.Id)).Any(i => i.Id == uploaded.Id).ShouldBeTrue();
    }

    [Fact]
    public async Task Delete_with_storage_down_still_deletes_the_record_and_enqueues_one_cleanup_job()
    {
        UseStorage();
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        var image = await UploadAsync(model.Id);

        UseStorage("http://127.0.0.1:1");
        await AsAdminAsync(() => Images.DeleteAsync(model.Id, image.Id));

        (await ImagesOfAsync(model.Id)).ShouldBeEmpty();
        (await BackgroundJobCountAsync()).ShouldBe(1);
    }
}
