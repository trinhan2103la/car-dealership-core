using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace CarDealership.CoreService.Catalog.Brands;

public class BrandDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public BrandStatus Status { get; set; }
    public int DisplayOrder { get; set; }
    public string? LogoKey { get; set; }
    public bool IsDemo { get; set; }

    /// <summary>Gửi lại nguyên giá trị này khi sửa để phát hiện xung đột phiên bản (409).</summary>
    public string ConcurrencyStamp { get; set; } = string.Empty;

    public DateTime CreationTime { get; set; }
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// Dữ liệu tạo/sửa Brand. Mọi trường kiểm ở domain và trả lỗi THEO TỪNG TRƯỜNG (400), nên không đặt
/// DataAnnotations ở đây (DataAnnotations sẽ dừng ở lỗi đầu tiên và tách lỗi thành hai nguồn).
/// </summary>
public class SaveBrandInput
{
    public string? Name { get; set; }
    public string? Slug { get; set; }

    /// <summary>Chỉ dùng khi tạo (mặc định Active). Đổi trạng thái sau đó qua endpoint <c>/status</c>.</summary>
    public BrandStatus? Status { get; set; }

    public int DisplayOrder { get; set; }
    public string? LogoKey { get; set; }

    /// <summary>Tùy chọn khi sửa: có gửi thì lệch phiên bản trả 409.</summary>
    public string? ConcurrencyStamp { get; set; }
}

public class BrandListInput
{
    /// <summary>Tìm tên Brand dạng chứa chuỗi, không phân biệt hoa thường và dấu tiếng Việt (Req 7.8).</summary>
    public string? Keyword { get; set; }

    public BrandStatus? Status { get; set; }

    /// <summary>Bắt đầu từ 1.</summary>
    public int Page { get; set; } = 1;

    /// <summary>Mặc định 20, tối đa 100.</summary>
    public int PageSize { get; set; } = 20;
}

public class ChangeBrandStatusInput
{
    public BrandStatus Status { get; set; }
    public string? ConcurrencyStamp { get; set; }
}

/// <summary>Quản trị Brand. Mọi thao tác cần Permission <c>Catalog.Edit</c>.</summary>
public interface IBrandAppService : IApplicationService
{
    Task<PagedResultDto<BrandDto>> GetListAsync(BrandListInput input);

    /// <summary>404 nếu không tồn tại.</summary>
    Task<BrandDto> GetAsync(Guid id);

    /// <summary>400 lỗi theo trường; 409 trùng tên/Slug (kể cả đồng thời).</summary>
    Task<BrandDto> CreateAsync(SaveBrandInput input);

    Task<BrandDto> UpdateAsync(Guid id, SaveBrandInput input);

    /// <summary>422 nếu chuyển Inactive khi còn CarModel Published (kèm danh sách tên và Slug).</summary>
    Task<BrandDto> ChangeStatusAsync(Guid id, ChangeBrandStatusInput input);
}
