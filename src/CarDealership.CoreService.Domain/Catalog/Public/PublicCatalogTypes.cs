using System;
using System.Collections.Generic;

namespace CarDealership.CoreService.Catalog.Public;

/// <summary>Tiêu chí sắp xếp danh sách xe công khai (Req 2.12). Mặc định là <see cref="Newest"/>; hòa thì Slug tăng dần.</summary>
public enum PublicCatalogSort
{
    /// <summary>Thời điểm Published gần nhất giảm dần.</summary>
    Newest = 0,

    /// <summary>Reference_Price thấp nhất của các variant Published, tăng dần.</summary>
    PriceAsc = 1,

    PriceDesc = 2,

    /// <summary>Tên Brand + tên CarModel A–Z.</summary>
    NameAsc = 3,
}

/// <summary>
/// Bộ lọc đã được validate ở tầng Application: AND giữa các tiêu chí, OR giữa các giá trị trong cùng một tiêu chí (Req 2.10).
/// <see cref="Keyword"/> chưa chuẩn hóa; repository dùng <c>VietnameseText.Normalize</c>.
/// </summary>
public sealed record PublicCatalogFilter(
    string? Keyword,
    IReadOnlyList<Guid>? BrandIds,
    IReadOnlyList<string>? BodyTypes,
    IReadOnlyList<string>? Fuels,
    IReadOnlyList<string>? Transmissions,
    IReadOnlyList<int>? Seats,
    long? MinPrice,
    long? MaxPrice,
    PublicCatalogSort Sort,
    int Page,
    int PageSize);

/// <summary>Thẻ xe trong danh sách công khai (Req 2.2). Key_Spec là tập giá trị khác nhau của các variant Published.</summary>
public sealed record PublicCarModelRow(
    Guid Id,
    string BrandName,
    string BrandSlug,
    string Name,
    string Slug,
    long? MinPrice,
    long? MaxPrice,
    string? CoverStorageKey,
    string? CoverAlt,
    bool IsFeatured,
    DateTime? PublishedAt,
    IReadOnlyList<string> BodyTypes,
    IReadOnlyList<string> Fuels,
    IReadOnlyList<string> Transmissions,
    IReadOnlyList<int> Seats);

public sealed record PublicCatalogPage(IReadOnlyList<PublicCarModelRow> Items, long TotalItems);

/// <summary>Một dòng sitemap: chỉ CarModel Published.</summary>
public sealed record SitemapEntry(string BrandSlug, string ModelSlug, DateTime? LastModifiedUtc);

/// <summary>
/// Kết quả tra cứu theo cặp Slug. <see cref="IsRedirect"/> = một trong hai Slug là Slug cũ: BFF trả 301 MỘT bước tới
/// <c>/xe/{BrandSlug}/{ModelSlug}</c> (Slug hiện tại). Không bao giờ chứa CarModel không Published.
/// </summary>
public sealed record SlugResolution(
    CarModels.CarModel CarModel,
    string BrandName,
    string BrandSlug,
    string ModelSlug,
    bool IsRedirect);
