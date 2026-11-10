using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace CarDealership.CoreService.Catalog.Public;

/// <summary>
/// Tham số danh sách xe công khai (Req 2). Tham số lặp cho OR trong cùng tiêu chí:
/// <c>?brand=toyota&amp;brand=honda&amp;fuel=Xăng</c>. Sai bất kỳ tham số nào → 400 nêu tên từng tham số, không trả danh sách.
/// </summary>
public class PublicCarModelListInput
{
    public string? Q { get; set; }

    /// <summary>Slug Brand. Slug không tồn tại → 400 (cần DB để kiểm, Req 2.17).</summary>
    public List<string>? Brand { get; set; }

    public List<string>? BodyType { get; set; }
    public List<string>? Fuel { get; set; }
    public List<string>? Transmission { get; set; }
    public List<int>? Seats { get; set; }

    public long? MinPrice { get; set; }
    public long? MaxPrice { get; set; }

    /// <summary><c>newest</c> (mặc định), <c>price_asc</c>, <c>price_desc</c>, <c>name_asc</c>.</summary>
    public string? Sort { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = CatalogConsts.PublicPageSizeDefault;
}

public class PublicCarCardDto
{
    public Guid Id { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public string BrandSlug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    /// <summary>Khoảng Reference_Price của các variant Published (bỏ giá liên hệ); null cả hai nếu chỉ có giá liên hệ.</summary>
    public long? MinPrice { get; set; }

    public long? MaxPrice { get; set; }

    /// <summary>null: không có ảnh cover (Public_Website hiển thị placeholder).</summary>
    public string? CoverStorageKey { get; set; }

    public string? CoverAlt { get; set; }
    public bool IsFeatured { get; set; }
    public DateTime? PublishedAt { get; set; }
    public List<string> BodyTypes { get; set; } = new();
    public List<string> Fuels { get; set; } = new();
    public List<string> Transmissions { get; set; } = new();
    public List<int> Seats { get; set; } = new();
}

public class PublicCarListDto
{
    public List<PublicCarCardDto> Items { get; set; } = new();
    public long TotalItems { get; set; }
    public int TotalPages { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class PublicExtraSpecDto
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Unit { get; set; }
}

public class PublicExtraSpecGroupDto
{
    public string Group { get; set; } = string.Empty;
    public List<PublicExtraSpecDto> Items { get; set; } = new();
}

public class PublicCarVariantDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? ReferencePrice { get; set; }
    public bool ContactForPrice { get; set; }
    public string BodyType { get; set; } = string.Empty;
    public string Fuel { get; set; } = string.Empty;
    public string Transmission { get; set; } = string.Empty;
    public int Seats { get; set; }
    public string? Engine { get; set; }
    public string? Power { get; set; }

    /// <summary>Chỉ các nhóm có ít nhất một giá trị (Req 3.4).</summary>
    public List<PublicExtraSpecGroupDto> ExtraSpecGroups { get; set; } = new();
}

public class PublicImageDto
{
    public string StorageKey { get; set; } = string.Empty;
    public string Alt { get; set; } = string.Empty;
    public bool IsCover { get; set; }
}

public class PublicCarModelDetailDto
{
    public Guid Id { get; set; }
    public string BrandName { get; set; } = string.Empty;
    public string BrandSlug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long? MinPrice { get; set; }
    public long? MaxPrice { get; set; }

    /// <summary>Theo thứ tự hiển thị, ảnh cover luôn ở đầu (Req 3.2).</summary>
    public List<PublicImageDto> Images { get; set; } = new();

    /// <summary>Chỉ variant Published (Req 3.3).</summary>
    public List<PublicCarVariantDto> Variants { get; set; } = new();
}

/// <summary>
/// Tra theo cặp Slug: hoặc <see cref="CarModel"/> (Slug hiện tại), hoặc <see cref="Redirect"/> (một trong hai Slug đã cũ,
/// BFF trả 301 MỘT bước tới URL này, Req 3.9). Không tồn tại / không Published → 404.
/// </summary>
public class PublicCarModelLookupDto
{
    public string? Redirect { get; set; }
    public PublicCarModelDetailDto? CarModel { get; set; }
}

public class PublicBrandDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? LogoKey { get; set; }
    public int DisplayOrder { get; set; }
}

public class SitemapEntryDto
{
    /// <summary>Đường dẫn tương đối <c>/xe/{brandSlug}/{modelSlug}</c>.</summary>
    public string Path { get; set; } = string.Empty;

    public DateTime? LastModified { get; set; }
}

/// <summary>Đọc danh mục công khai: CHỈ dữ liệu Published. Gọi qua Service_Credential (không cần phiên Admin).</summary>
public interface IPublicCatalogAppService : IApplicationService
{
    Task<PublicCarListDto> GetCarModelsAsync(PublicCarModelListInput input);

    Task<PublicCarModelLookupDto> GetCarModelBySlugsAsync(string? brandSlug, string? modelSlug);

    /// <summary>CarModel nổi bật cho trang chủ (tối đa 8).</summary>
    Task<List<PublicCarCardDto>> GetHomeCarsAsync();

    /// <summary>Brand Active theo thứ tự hiển thị.</summary>
    Task<List<PublicBrandDto>> GetBrandsAsync();

    Task<List<SitemapEntryDto>> GetSitemapAsync();
}
