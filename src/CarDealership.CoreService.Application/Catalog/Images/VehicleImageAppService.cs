using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Audit;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Identity.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace CarDealership.CoreService.Catalog.Images;

/// <summary>
/// Upload ảnh xe. Nguyên tắc: ghi storage trước, ghi bản ghi sau; DB lỗi thì xóa object vừa ghi.
/// Core KHÔNG giải mã ảnh (BFF đã xử lý), chỉ kiểm chữ ký WebP và kích thước.
/// </summary>
[Authorize(AppPermissions.CatalogEdit)]
public class VehicleImageAppService : CoreServiceAppService, IVehicleImageAppService
{
    private const long MaxVariantBytes = 5 * 1024 * 1024;

    private readonly ICarModelRepository _carModels;
    private readonly IRepository<Brand, Guid> _brands;
    private readonly VehicleImageManager _images;
    private readonly IImageStorage _storage;
    private readonly IAuditTrail _audit;
    private readonly IBackgroundJobManager _jobs;

    public VehicleImageAppService(
        ICarModelRepository carModels,
        IRepository<Brand, Guid> brands,
        VehicleImageManager images,
        IImageStorage storage,
        IAuditTrail audit,
        IBackgroundJobManager jobs)
    {
        _carModels = carModels;
        _brands = brands;
        _images = images;
        _storage = storage;
        _audit = audit;
        _jobs = jobs;
    }

    public virtual async Task<VehicleImageDto> UploadAsync(Guid carModelId, UploadVehicleImageInput input)
    {
        // 1) Kiểm trước khi ghi file để không tạo object rác cho model không tồn tại / Archived.
        var model = await _carModels.FindAsync(carModelId, includeDetails: false)
                    ?? throw new EntityNotFoundException(typeof(CarModel), carModelId);
        if (model.Status == CatalogStatus.Archived)
        {
            throw CatalogExceptions.CarModelArchived();
        }

        var variants = new (string Size, IRemoteStreamContent? Content)[]
        {
            ("sm", input.Sm), ("md", input.Md), ("lg", input.Lg)
        };
        var errors = new FieldErrors();
        var buffers = new Dictionary<string, byte[]>();
        foreach (var (size, content) in variants)
        {
            var bytes = content is null ? null : await ReadLimitedAsync(content, MaxVariantBytes);
            if (bytes is null || !IsWebP(bytes))
            {
                errors.Add(size, "Phải là ảnh WebP hợp lệ.");
            }
            else
            {
                buffers[size] = bytes;
            }
        }
        errors.ThrowIfAny();

        // 2) Ghi 3 object. Lỗi storage → xóa phần đã ghi, trả 503 cho phép thử lại, KHÔNG có bản ghi.
        var baseKey = ImageStorageKeys.NewBaseKey();
        var written = new List<string>();
        try
        {
            foreach (var (size, bytes) in buffers)
            {
                var key = ImageStorageKeys.ObjectKey(carModelId, baseKey, size);
                using var stream = new MemoryStream(bytes);
                await _storage.PutAsync(key, stream, "image/webp");
                written.Add(key);
            }
        }
        catch (ImageStorageUnavailableException ex)
        {
            Logger.LogWarning(ex, "Ghi ảnh thất bại, dọn {Count} object", written.Count);
            await DeleteQuietlyAsync(written);
            throw new BusinessException(
                CatalogErrorCodes.StorageUnavailable,
                "Lưu ảnh tạm thời không khả dụng, hãy thử lại.");
        }

        // 3) Bản ghi: khóa FOR UPDATE + giới hạn 30 trong VehicleImageManager. Lỗi → xóa object.
        try
        {
            var (_, image) = await _images.AddAsync(carModelId, baseKey, input.Alt, input.CarVariantId);
            await _audit.WriteAsync(
                AuditActions.Create,
                CatalogEntityTypes.VehicleImage,
                image.Id.ToString(),
                null,
                CatalogMapping.Snapshot(image));
            await CurrentUnitOfWork!.SaveChangesAsync(); // lỗi DB nổ ra ở đây, vẫn trong try
            return CatalogMapping.ToDto(image);
        }
        catch
        {
            await DeleteQuietlyAsync(written);
            throw;
        }
        // Commit thất bại SAU khi method trả về (hiếm) có thể để lại object mồ côi: chấp nhận, ghi ở ADR.
    }

    /// <summary>
    /// Đặt lại thứ tự (đủ mọi ảnh, Req 9.6) và tùy chọn đổi cover (tối đa một, Req 9.7).
    /// Một Audit_Log <c>Update</c> cho CarModel, trường <c>images</c> là danh sách id theo thứ tự.
    /// </summary>
    public virtual async Task<List<VehicleImageDto>> ReorderAsync(Guid carModelId, ReorderImagesInput input)
    {
        var model = await LoadAsync(carModelId, lockForUpdate: true);
        if (input.CoverImageId is { } coverId && model.Images.All(i => i.Id != coverId))
        {
            new FieldErrors().Add("coverImageId", "Ảnh cover không thuộc mẫu xe này.").ThrowIfAny();
        }

        var before = ImagesSnapshot(model);
        model.ReorderImages(input.ImageIds ?? new List<Guid>());

        if (input.CoverImageId is { } newCover && model.Images.Single(i => i.Id == newCover) is { IsCover: false })
        {
            // Hai bước có lưu ở giữa: partial unique index (car_model_id) WHERE is_cover kiểm từng câu UPDATE.
            if (model.ClearCover())
            {
                await _carModels.UpdateAsync(model, autoSave: true);
            }

            model.SetCover(newCover);
        }

        await _carModels.UpdateAsync(model, autoSave: true);
        await _audit.WriteAsync(AuditActions.Update, CatalogEntityTypes.CarModel, model.Id.ToString(),
            before, ImagesSnapshot(model));

        return model.Images.OrderBy(i => i.DisplayOrder).Select(CatalogMapping.ToDto).ToList();
    }

    /// <summary>Sửa alt. Model Published mà alt mới không đạt 5–150 ký tự → 422 (Req 10.10).</summary>
    public virtual async Task<VehicleImageDto> UpdateAsync(Guid carModelId, Guid imageId, UpdateImageInput input)
    {
        var model = await LoadAsync(carModelId, lockForUpdate: true);
        var brand = await _brands.GetAsync(model.BrandId);
        var before = CatalogMapping.Snapshot(model.GetImage(imageId));

        var image = model.UpdateImageAlt(imageId, input.Alt, brand.Status);

        await _carModels.UpdateAsync(model, autoSave: true);
        await _audit.WriteAsync(AuditActions.Update, CatalogEntityTypes.VehicleImage, image.Id.ToString(),
            before, CatalogMapping.Snapshot(image));
        return CatalogMapping.ToDto(image);
    }

    /// <summary>
    /// Gỡ ảnh (thứ tự còn lại liên tục). Object chỉ bị xóa SAU KHI COMMIT: DB rollback thì file vẫn còn.
    /// Storage lỗi lúc xóa → đưa vào job <see cref="ImageCleanupJob"/> để thử lại (Req 9.9).
    /// </summary>
    public virtual async Task DeleteAsync(Guid carModelId, Guid imageId)
    {
        var model = await LoadAsync(carModelId, lockForUpdate: true);
        var brand = await _brands.GetAsync(model.BrandId);
        var before = CatalogMapping.Snapshot(model.GetImage(imageId));

        var image = model.RemoveImage(imageId, brand.Status);

        await _carModels.UpdateAsync(model, autoSave: true);
        await _audit.WriteAsync(AuditActions.Delete, CatalogEntityTypes.VehicleImage, image.Id.ToString(), before, null);

        var keys = ImageStorageKeys.Sizes
            .Select(size => ImageStorageKeys.ObjectKey(carModelId, image.StorageKey, size))
            .ToList();
        CurrentUnitOfWork!.OnCompleted(() => DeleteObjectsOrEnqueueAsync(keys));
    }

    private async Task DeleteObjectsOrEnqueueAsync(List<string> keys)
    {
        try
        {
            foreach (var key in keys)
            {
                await _storage.DeleteAsync(key);
            }
        }
        catch (ImageStorageUnavailableException ex)
        {
            Logger.LogWarning(ex, "Xóa {Count} object thất bại, đưa vào hàng đợi dọn", keys.Count);
            // UoW của request đã commit xong: ghi job trong một UoW riêng.
            using var uow = UnitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = false }, requiresNew: true);
            await _jobs.EnqueueAsync(new ImageCleanupArgs { Keys = keys });
            await uow.CompleteAsync();
        }
    }

    private async Task<CarModel> LoadAsync(Guid id, bool lockForUpdate) =>
        await _carModels.FindWithDetailsAsync(id, lockForUpdate)
        ?? throw new EntityNotFoundException(typeof(CarModel), id);

    private static IReadOnlyDictionary<string, object?> ImagesSnapshot(CarModel model) => new Dictionary<string, object?>
    {
        ["images"] = model.Images.OrderBy(i => i.DisplayOrder).Select(i => i.Id).ToList(),
        ["coverImageId"] = model.Images.FirstOrDefault(i => i.IsCover)?.Id,
    };

    private static bool IsWebP(byte[] b) =>
        b.Length >= 12
        && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F'
        && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P';

    /// <summary>Đọc tối đa <paramref name="max"/> byte; vượt thì trả null (không đọc hết stream lớn vào RAM).</summary>
    private static async Task<byte[]?> ReadLimitedAsync(IRemoteStreamContent content, long max)
    {
        await using var source = content.GetStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > max)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.Length == 0 ? null : buffer.ToArray();
    }

    private async Task DeleteQuietlyAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try
            {
                await _storage.DeleteAsync(key);
            }
            catch (ImageStorageUnavailableException ex)
            {
                Logger.LogWarning(ex, "Không xóa được object mồ côi");
            }
        }
    }
}
