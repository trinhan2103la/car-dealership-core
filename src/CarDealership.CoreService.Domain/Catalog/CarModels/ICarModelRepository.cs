using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.Public;
using Volo.Abp.Domain.Repositories;

namespace CarDealership.CoreService.Catalog.CarModels;

/// <summary>Bộ lọc danh sách quản trị (Admin_Portal). Tìm theo tên không dấu trên <c>search_text</c>.</summary>
public sealed record AdminCarModelFilter(Guid? BrandId, CatalogStatus? Status, string? Keyword, int Skip, int Take);

/// <summary>
/// Repository DUY NHẤT của aggregate CarModel (kèm CarVariant và VehicleImage). Cố ý KHÔNG có repository riêng cho
/// variant hay ảnh, vì sửa con mà không qua root sẽ phá ranh giới aggregate.
/// </summary>
public interface ICarModelRepository : IRepository<CarModel, Guid>
{
    /// <summary>
    /// Load aggregate đầy đủ (variant + ảnh), tracked. <paramref name="lockForUpdate"/> = true thì khóa hàng CarModel
    /// (<c>SELECT … FOR UPDATE</c>) trước khi đọc, để các thao tác đồng thời trên cùng model (đếm 30 ảnh) được tuần tự hóa.
    /// Chỉ có tác dụng trong transaction.
    /// </summary>
    Task<CarModel?> FindWithDetailsAsync(Guid id, bool lockForUpdate = false, CancellationToken cancellationToken = default);

    /// <summary>Mọi CarModel của Brand kèm variant (để tính lại search_text khi Brand đổi tên), tracked.</summary>
    Task<List<CarModel>> GetListByBrandWithVariantsAsync(Guid brandId, CancellationToken cancellationToken = default);

    /// <summary>Tên và Slug của các CarModel Published thuộc Brand (Req 7.5). Không tracked.</summary>
    Task<List<BlockingCarModel>> GetPublishedSummariesByBrandAsync(Guid brandId, CancellationToken cancellationToken = default);

    /// <summary>Tên trùng trong Brand (không phân biệt hoa thường, đã trim).</summary>
    Task<bool> NameExistsInBrandAsync(Guid brandId, string name, Guid? exceptId, CancellationToken cancellationToken = default);

    Task<bool> SlugExistsInBrandAsync(Guid brandId, string slug, Guid? exceptId, CancellationToken cancellationToken = default);

    Task<(List<CarModel> Items, long Total)> GetAdminListAsync(AdminCarModelFilter filter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tra CarModel Published theo cặp (brandSlug, modelSlug) với Slug hiện tại hoặc Slug cũ (3 bước của design).
    /// Trả null nếu không tồn tại hoặc không Published (không tiết lộ trạng thái). Không có tra cứu chỉ bằng modelSlug.
    /// </summary>
    Task<SlugResolution?> FindPublishedBySlugsAsync(string brandSlug, string modelSlug, CancellationToken cancellationToken = default);

    Task<PublicCatalogPage> SearchPublishedAsync(PublicCatalogFilter filter, CancellationToken cancellationToken = default);

    /// <summary>CarModel Published nổi bật (Req 1) cho trang chủ, tối đa <paramref name="take"/>.</summary>
    Task<IReadOnlyList<PublicCarModelRow>> GetFeaturedPublishedAsync(int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SitemapEntry>> GetSitemapEntriesAsync(CancellationToken cancellationToken = default);
}
