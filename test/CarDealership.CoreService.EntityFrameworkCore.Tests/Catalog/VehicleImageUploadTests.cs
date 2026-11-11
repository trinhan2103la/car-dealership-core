using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CarDealership.CoreService.Audit;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Images;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Validation;
using Xunit;

namespace CarDealership.CoreService.Catalog;

/// <summary>Bọc một <see cref="IImageStorage"/> thật và ghi lại các key đã ghi, để test biết object nào được tạo.</summary>
public sealed class RecordingImageStorage : IImageStorage
{
    private readonly IImageStorage _inner;

    public RecordingImageStorage(IImageStorage inner)
    {
        _inner = inner;
    }

    public List<string> Puts { get; } = new();

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        await _inner.PutAsync(key, content, contentType, cancellationToken);
        Puts.Add(key);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        _inner.DeleteAsync(key, cancellationToken);

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        _inner.ExistsAsync(key, cancellationToken);
}

/// <summary>Upload ảnh xe qua Core (Req 9.1, 9.5, 9.13): ghi storage trước, bản ghi sau; lỗi nào cũng không để lại rác.</summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class VehicleImageUploadTests : CatalogTestBase, IClassFixture<S3MockFixture>
{
    private readonly S3MockFixture _s3;

    public VehicleImageUploadTests(S3MockFixture s3)
    {
        _s3 = s3;
    }

    private static S3ImageStorage S3(string? serviceUrl) => new(Options.Create(new ImageStorageOptions
    {
        ServiceUrl = serviceUrl,
        Bucket = S3MockFixture.Bucket,
        AccessKey = S3MockFixture.AccessKey,
        SecretKey = S3MockFixture.SecretKey,
        TimeoutSeconds = 3,
    }));

    /// <summary>Dùng S3Mock (hoặc storage chết) cho test này và trả về bản ghi lại các lần ghi.</summary>
    private RecordingImageStorage UseStorage(string? serviceUrl = null)
    {
        var recording = new RecordingImageStorage(S3(serviceUrl ?? _s3.ServiceUrl));
        GetRequiredService<ImageStorageSwitch>().Use(recording);
        return recording;
    }

    private IVehicleImageAppService Images => GetRequiredService<IVehicleImageAppService>();

    /// <summary>Chỉ cần chữ ký RIFF....WEBP: Core không giải mã ảnh.</summary>
    private static byte[] WebP(int extraBytes = 16)
    {
        var bytes = new byte[12 + extraBytes];
        "RIFF"u8.CopyTo(bytes);
        "WEBP"u8.CopyTo(bytes.AsSpan(8));
        return bytes;
    }

    private static byte[] Png() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0 };

    private static IRemoteStreamContent Content(byte[] bytes) => new RemoteStreamContent(new MemoryStream(bytes), "image.webp");

    private static UploadVehicleImageInput Input(byte[]? sm = null, byte[]? md = null, byte[]? lg = null, bool omitMd = false) => new()
    {
        Sm = Content(sm ?? WebP()),
        Md = omitMd ? null : Content(md ?? WebP()),
        Lg = Content(lg ?? WebP()),
        Alt = "Ảnh mặt trước của xe",
    };

    private Task<VehicleImageDto> UploadAsync(Guid modelId, UploadVehicleImageInput input) =>
        AsAdminAsync(() => Images.UploadAsync(modelId, input));

    private async Task<int> ImageCountAsync(Guid modelId) =>
        (await AsAdminAsync(() => CarModels.GetAsync(modelId))).Images.Count;

    [Fact]
    public async Task Valid_three_variants_create_the_record_the_objects_and_one_audit_entry()
    {
        var storage = UseStorage();
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);

        var dto = await UploadAsync(model.Id, Input());

        storage.Puts.Count.ShouldBe(3);
        foreach (var size in ImageStorageKeys.Sizes)
        {
            (await storage.ExistsAsync(ImageStorageKeys.ObjectKey(model.Id, dto.StorageKey, size))).ShouldBeTrue();
        }

        (await ImageCountAsync(model.Id)).ShouldBe(1);
        var audits = (await AuditEntriesAsync(dto.Id.ToString()))
            .Where(e => e.EntityType == CatalogEntityTypes.VehicleImage)
            .ToList();
        audits.Count.ShouldBe(1);
        audits[0].Action.ShouldBe(AuditActions.Create);
    }

    [Fact]
    public async Task Archived_model_is_rejected_before_anything_is_written()
    {
        var storage = UseStorage();
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        await AsAdminAsync(() => CarModels.ChangeStatusAsync(model.Id, new CarModels.ChangeCatalogStatusInput { Status = CatalogStatus.Archived }));

        var ex = await Should.ThrowAsync<BusinessException>(() => UploadAsync(model.Id, Input()));

        ex.Code.ShouldBe(CatalogErrorCodes.CarModelArchived);
        storage.Puts.ShouldBeEmpty();
        (await ImageCountAsync(model.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Unreachable_storage_gives_503_code_and_no_record()
    {
        UseStorage("http://127.0.0.1:1");
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);

        var ex = await Should.ThrowAsync<BusinessException>(() => UploadAsync(model.Id, Input()));

        ex.Code.ShouldBe(CatalogErrorCodes.StorageUnavailable);
        (await ImageCountAsync(model.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Record_failure_at_the_30_image_limit_removes_the_three_objects_just_written()
    {
        var storage = UseStorage();
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);
        for (var i = 0; i < 30; i++)
        {
            await AddImageAsync(model.Id);
        }

        var ex = await Should.ThrowAsync<BusinessException>(() => UploadAsync(model.Id, Input()));

        ex.Code.ShouldBe(CatalogErrorCodes.ImageLimitReached);
        storage.Puts.Count.ShouldBe(3);
        foreach (var key in storage.Puts)
        {
            (await storage.ExistsAsync(key)).ShouldBeFalse();
        }

        (await ImageCountAsync(model.Id)).ShouldBe(30);
    }

    [Fact]
    public async Task Missing_md_is_a_validation_error_on_field_md_and_writes_nothing()
    {
        var storage = UseStorage();
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);

        var ex = await Should.ThrowAsync<AbpValidationException>(() => UploadAsync(model.Id, Input(omitMd: true)));

        ex.ValidationErrors.SelectMany(e => e.MemberNames).ShouldBe(new[] { "md" });
        storage.Puts.ShouldBeEmpty();
        (await ImageCountAsync(model.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task A_png_variant_is_rejected_on_its_own_field()
    {
        var storage = UseStorage();
        var brand = await CreateBrandAsync();
        var model = await CreateModelAsync(brand.Id);

        var ex = await Should.ThrowAsync<AbpValidationException>(() => UploadAsync(model.Id, Input(lg: Png())));

        ex.ValidationErrors.SelectMany(e => e.MemberNames).ShouldBe(new[] { "lg" });
        storage.Puts.ShouldBeEmpty();
    }
}
