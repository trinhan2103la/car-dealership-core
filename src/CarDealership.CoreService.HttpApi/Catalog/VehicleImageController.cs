using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Images;
using CarDealership.CoreService.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace CarDealership.CoreService.Catalog;

/// <summary>
/// Ảnh xe (chỉ BFF gọi, Service_Credential scope=bff). Quyền <c>Catalog.Edit</c> kiểm ở <see cref="IVehicleImageAppService"/>:
/// 400 thiếu/sai biến thể WebP (lỗi theo tên trường sm/md/lg), 404 model không tồn tại,
/// 422 model Archived hoặc đã đủ 30 ảnh, 503 Object_Storage không khả dụng (client thử lại được).
/// </summary>
[Route("api/catalog/car-models/{carModelId:guid}/images")]
public class VehicleImageController : CoreServiceController
{
    private readonly IVehicleImageAppService _images;

    public VehicleImageController(IVehicleImageAppService images)
    {
        _images = images;
    }

    [HttpPost]
    [RequestSizeLimit(16 * 1024 * 1024)] // 3 WebP đã nén, dư sức
    public virtual Task<VehicleImageDto> UploadAsync(Guid carModelId, [FromForm] UploadVehicleImageInput input) =>
        _images.UploadAsync(carModelId, input);

    /// <summary>Đặt lại thứ tự (đủ mọi ảnh) và tùy chọn đổi cover.</summary>
    [HttpPut("order")]
    public virtual Task<List<VehicleImageDto>> ReorderAsync(Guid carModelId, [FromBody] ReorderImagesInput input) =>
        _images.ReorderAsync(carModelId, input);

    /// <summary>Sửa alt của một ảnh.</summary>
    [HttpPut("{imageId:guid}")]
    public virtual Task<VehicleImageDto> UpdateAsync(Guid carModelId, Guid imageId, [FromBody] UpdateImageInput input) =>
        _images.UpdateAsync(carModelId, imageId, input);

    /// <summary>Xóa ảnh. Object trên Object_Storage bị xóa sau khi commit (lỗi thì thử lại bằng job).</summary>
    [HttpDelete("{imageId:guid}")]
    public virtual Task DeleteAsync(Guid carModelId, Guid imageId) =>
        _images.DeleteAsync(carModelId, imageId);
}
