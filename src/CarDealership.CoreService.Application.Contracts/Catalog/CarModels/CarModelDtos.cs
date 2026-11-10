using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace CarDealership.CoreService.Catalog.CarModels;

public class ExtraSpecDto
{
    public string? Group { get; set; }
    public string? Name { get; set; }
    public string? Value { get; set; }
    public string? Unit { get; set; }
}

public class CarVariantDto
{
    public Guid Id { get; set; }
    public Guid CarModelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public CatalogStatus Status { get; set; }
    public long? ReferencePrice { get; set; }
    public bool ContactForPrice { get; set; }
    public string BodyType { get; set; } = string.Empty;
    public string Fuel { get; set; } = string.Empty;
    public string Transmission { get; set; } = string.Empty;
    public int Seats { get; set; }
    public string? Engine { get; set; }
    public string? Power { get; set; }
    public List<ExtraSpecDto> ExtraSpecs { get; set; } = new();
    public bool IsDemo { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class VehicleImageDto
{
    public Guid Id { get; set; }
    public Guid? CarVariantId { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public string Alt { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsCover { get; set; }
}

public class CarModelSummaryDto
{
    public Guid Id { get; set; }
    public Guid BrandId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public CatalogStatus Status { get; set; }
    public bool IsFeatured { get; set; }
    public int DisplayOrder { get; set; }
    public long? MinPublishedPrice { get; set; }
    public long? MaxPublishedPrice { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

public class CarModelDto : CarModelSummaryDto
{
    public string? Description { get; set; }
    public bool IsDemo { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
    public List<CarVariantDto> Variants { get; set; } = new();
    public List<VehicleImageDto> Images { get; set; } = new();
}

/// <summary>Tạo CarModel (luôn ở Draft, Req 8.1). Kiểm ở domain, lỗi trả theo từng trường (400).</summary>
public class CreateCarModelInput
{
    public Guid BrandId { get; set; }
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public bool IsFeatured { get; set; }
    public int DisplayOrder { get; set; }
}

public class UpdateCarModelInput
{
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public bool IsFeatured { get; set; }
    public int DisplayOrder { get; set; }

    /// <summary>Bắt buộc: stamp đọc được lần trước. Lệch → 409 (Req 8.11).</summary>
    public string? ConcurrencyStamp { get; set; }
}

public class CarModelListInput
{
    public Guid? BrandId { get; set; }
    public CatalogStatus? Status { get; set; }

    /// <summary>Tìm không dấu trên tên Brand + model + variant Published.</summary>
    public string? Keyword { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

/// <summary>Dữ liệu tạo/sửa CarVariant (Req 8.3–8.8). Giá decimal để báo lỗi "không phải số nguyên" theo trường.</summary>
public class SaveCarVariantInput
{
    public string? Name { get; set; }
    public string? BodyType { get; set; }
    public string? Fuel { get; set; }
    public string? Transmission { get; set; }
    public int? Seats { get; set; }
    public string? Engine { get; set; }
    public string? Power { get; set; }
    public decimal? ReferencePrice { get; set; }
    public bool ContactForPrice { get; set; }
    public List<ExtraSpecDto>? ExtraSpecs { get; set; }

    /// <summary>Khi sửa: stamp của VARIANT đọc được lần trước; lệch → 409.</summary>
    public string? ConcurrencyStamp { get; set; }
}

public class ChangeCatalogStatusInput
{
    public CatalogStatus Status { get; set; }
    public string? ConcurrencyStamp { get; set; }
}

/// <summary>Kết quả kiểm xuất bản (không đổi trạng thái): đủ checklist mọi quy tắc, đạt/chưa đạt (Req 10.6).</summary>
public class PublishCheckDto
{
    public bool CanPublish { get; set; }
    public List<PublishRuleResultDto> Rules { get; set; } = new();
    public List<Guid> InvalidImageIds { get; set; } = new();
}

public class PublishRuleResultDto
{
    public string Code { get; set; } = string.Empty;
    public bool Passed { get; set; }
}

/// <summary>Quản trị CarModel. Sửa nội dung cần <c>Catalog.Edit</c>; chuyển trạng thái cần <c>Catalog.Publish</c>.</summary>
public interface ICarModelAppService : IApplicationService
{
    Task<PagedResultDto<CarModelSummaryDto>> GetListAsync(CarModelListInput input);

    Task<CarModelDto> GetAsync(Guid id);

    Task<CarModelDto> CreateAsync(CreateCarModelInput input);

    Task<CarModelDto> UpdateAsync(Guid id, UpdateCarModelInput input);

    /// <summary><c>Catalog.Publish</c>. 422 khi chuyển không hợp lệ hoặc Publish_Validation thất bại (đủ mọi quy tắc).</summary>
    Task<CarModelDto> ChangeStatusAsync(Guid id, ChangeCatalogStatusInput input);

    /// <summary><c>Catalog.Edit</c>. Không đổi trạng thái.</summary>
    Task<PublishCheckDto> GetPublishCheckAsync(Guid id);
}

/// <summary>CarVariant chỉ truy cập qua aggregate CarModel. Sửa cần <c>Catalog.Edit</c>; chuyển trạng thái cần <c>Catalog.Publish</c>.</summary>
public interface ICarVariantAppService : IApplicationService
{
    Task<List<CarVariantDto>> GetListAsync(Guid carModelId);

    Task<CarVariantDto> GetAsync(Guid carModelId, Guid variantId);

    Task<CarVariantDto> CreateAsync(Guid carModelId, SaveCarVariantInput input);

    Task<CarVariantDto> UpdateAsync(Guid carModelId, Guid variantId, SaveCarVariantInput input);

    /// <summary>422: chuyển không hợp lệ, Publish_Validation của variant, hoặc hạ variant Published cuối của model Published.</summary>
    Task<CarVariantDto> ChangeStatusAsync(Guid carModelId, Guid variantId, ChangeCatalogStatusInput input);
}
