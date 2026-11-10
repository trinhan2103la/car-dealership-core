using System;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.CarModels;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Services;

namespace CarDealership.CoreService.Catalog.Images;

/// <summary>
/// Thêm ảnh và đổi cover cho CarModel. Giới hạn 30 ảnh phải đúng cả khi nhiều upload đến ĐỒNG THỜI (Req 9.5): khóa hàng
/// CarModel bằng <c>SELECT … FOR UPDATE</c> trước khi load aggregate, nên request thứ hai chờ request thứ nhất commit rồi mới
/// đếm. Phải chạy trong UoW CÓ transaction (request không phải GET), nếu không khóa nhả ngay và giới hạn có thể bị vượt.
/// </summary>
public class VehicleImageManager : DomainService
{
    private readonly ICarModelRepository _carModels;

    public VehicleImageManager(ICarModelRepository carModels)
    {
        _carModels = carModels;
    }

    public async Task<(CarModel Model, VehicleImage Image)> AddAsync(
        Guid carModelId, string storageKey, string? alt, Guid? carVariantId = null)
    {
        var model = await _carModels.FindWithDetailsAsync(carModelId, lockForUpdate: true)
                    ?? throw new EntityNotFoundException(typeof(CarModel), carModelId);

        var image = model.AddImage(GuidGenerator.Create(), storageKey, alt, carVariantId);
        await _carModels.UpdateAsync(model, autoSave: true);
        return (model, image);
    }

    /// <summary>
    /// Đặt ảnh cover, bỏ cover cũ (Req 9.7). Hai bước có lưu ở giữa vì partial unique index <c>(car_model_id) WHERE is_cover</c>
    /// kiểm từng câu UPDATE mà EF không bảo đảm thứ tự (bỏ cờ cũ phải xuống DB trước khi bật cờ mới).
    /// </summary>
    public async Task<CarModel> SetCoverAsync(Guid carModelId, Guid imageId)
    {
        var model = await _carModels.FindWithDetailsAsync(carModelId, lockForUpdate: true)
                    ?? throw new EntityNotFoundException(typeof(CarModel), carModelId);

        model.GetImage(imageId); // 404 trước khi đổi gì
        if (model.ClearCover())
        {
            await _carModels.UpdateAsync(model, autoSave: true);
        }

        model.SetCover(imageId);
        await _carModels.UpdateAsync(model, autoSave: true);
        return model;
    }
}
