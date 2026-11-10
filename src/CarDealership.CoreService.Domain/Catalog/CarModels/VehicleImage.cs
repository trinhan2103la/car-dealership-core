using System;
using Volo.Abp.Domain.Entities;

namespace CarDealership.CoreService.Catalog.CarModels;

/// <summary>
/// Entity con của <see cref="CarModel"/> (bảng <c>cat_vehicle_images</c>, Req 9). Không có repository riêng.
/// <see cref="StorageKey"/> là khóa object ngẫu nhiên (UUID), tuyệt đối không chứa tên file gốc. Alt tối đa 150 ký tự
/// khi lưu; 5–150 chỉ BẮT BUỘC khi xuất bản CarModel (Req 9.11).
/// </summary>
public class VehicleImage : Entity<Guid>
{
    public Guid CarModelId { get; private set; }

    /// <summary>Gắn ảnh với một variant cụ thể (tùy chọn).</summary>
    public Guid? CarVariantId { get; private set; }

    public string StorageKey { get; private set; } = string.Empty;

    public string Alt { get; private set; } = string.Empty;

    public int DisplayOrder { get; private set; }

    public bool IsCover { get; private set; }

    private VehicleImage()
    {
        // EF Core
    }

    internal VehicleImage(Guid id, Guid carModelId, Guid? carVariantId, string storageKey, string alt, int displayOrder)
        : base(id)
    {
        CarModelId = carModelId;
        CarVariantId = carVariantId;
        StorageKey = storageKey;
        Alt = alt;
        DisplayOrder = displayOrder;
    }

    internal void SetCover(bool isCover) => IsCover = isCover;

    internal void SetDisplayOrder(int displayOrder) => DisplayOrder = displayOrder;

    /// <summary>Alt hợp lệ để xuất bản: sau khi trim dài 5–150 ký tự.</summary>
    public bool HasPublishableAlt()
    {
        var length = Alt.Trim().Length;
        return length is >= CatalogConsts.ImageAltMinLength and <= CatalogConsts.ImageAltMaxLength;
    }
}
