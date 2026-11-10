using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Public;
using CarDealership.CoreService.Catalog.Slugs;
using CarDealership.CoreService.Text;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace CarDealership.CoreService.EntityFrameworkCore.Catalog;

/// <summary>Repository của aggregate CarModel (kèm variant và ảnh). Xem <see cref="ICarModelRepository"/>.</summary>
public class CarModelRepository : EfCoreRepository<CoreServiceDbContext, CarModel, Guid>, ICarModelRepository
{
    public CarModelRepository(IDbContextProvider<CoreServiceDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    // ------------------------------------------------------------------ aggregate

    public virtual async Task<CarModel?> FindWithDetailsAsync(
        Guid id, bool lockForUpdate = false, CancellationToken cancellationToken = default)
    {
        var db = await GetDbContextAsync();

        if (lockForUpdate)
        {
            // Khóa hàng CarModel cho tới hết transaction: hai thao tác đồng thời trên cùng model xếp hàng ở đây, nên đếm
            // "đã có bao nhiêu ảnh" luôn thấy kết quả của thao tác trước. Ngoài transaction khóa nhả ngay (vô hiệu).
            await db.Database
                .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM cat_car_models WHERE id = {id} FOR UPDATE")
                .ToListAsync(cancellationToken);
        }

        return await db.CarModels
            .Include(m => m.Variants)
            .Include(m => m.Images)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public virtual async Task<List<CarModel>> GetListByBrandWithVariantsAsync(
        Guid brandId, CancellationToken cancellationToken = default)
    {
        var db = await GetDbContextAsync();
        return await db.CarModels
            .Include(m => m.Variants)
            .Where(m => m.BrandId == brandId)
            .ToListAsync(cancellationToken);
    }

    public virtual async Task<List<BlockingCarModel>> GetPublishedSummariesByBrandAsync(
        Guid brandId, CancellationToken cancellationToken = default)
    {
        var db = await GetDbContextAsync();
        return await db.CarModels
            .AsNoTracking()
            .Where(m => m.BrandId == brandId && m.Status == CatalogStatus.Published)
            .OrderBy(m => m.Slug)
            .Select(m => new BlockingCarModel(m.Name, m.Slug))
            .ToListAsync(cancellationToken);
    }

    public virtual async Task<bool> NameExistsInBrandAsync(
        Guid brandId, string name, Guid? exceptId, CancellationToken cancellationToken = default)
    {
        var db = await GetDbContextAsync();
        var lowered = name.Trim().ToLower();
        return await db.CarModels.AnyAsync(
            m => m.BrandId == brandId && m.Name.ToLower() == lowered && (exceptId == null || m.Id != exceptId),
            cancellationToken);
    }

    public virtual async Task<bool> SlugExistsInBrandAsync(
        Guid brandId, string slug, Guid? exceptId, CancellationToken cancellationToken = default)
    {
        var db = await GetDbContextAsync();
        return await db.CarModels.AnyAsync(
            m => m.BrandId == brandId && m.Slug == slug && (exceptId == null || m.Id != exceptId),
            cancellationToken);
    }

    public virtual async Task<(List<CarModel> Items, long Total)> GetAdminListAsync(
        AdminCarModelFilter filter, CancellationToken cancellationToken = default)
    {
        var db = await GetDbContextAsync();
        var query = db.CarModels.AsNoTracking().AsQueryable();

        if (filter.BrandId is { } brandId)
        {
            query = query.Where(m => m.BrandId == brandId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(m => m.Status == status);
        }

        var keyword = VietnameseText.Normalize(filter.Keyword);
        if (keyword.Length > 0)
        {
            var pattern = $"%{EscapeLike(keyword)}%";
            query = query.Where(m => EF.Functions.ILike(m.SearchText, pattern, "\\"));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var items = await query
            .OrderBy(m => m.DisplayOrder)
            .ThenBy(m => m.Slug)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    // ------------------------------------------------------------------ công khai

    public virtual async Task<SlugResolution?> FindPublishedBySlugsAsync(
        string brandSlug, string modelSlug, CancellationToken cancellationToken = default)
    {
        var db = await GetDbContextAsync();
        brandSlug = Slug.Normalize(brandSlug);
        modelSlug = Slug.Normalize(modelSlug);

        // (1) Brand theo Slug hiện tại; không có thì tra lịch sử Slug của Brand (phạm vi UUID zero).
        var brandIsOld = false;
        var brand = await db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.Slug == brandSlug, cancellationToken);
        if (brand is null)
        {
            var oldBrandId = await db.SlugHistories.AsNoTracking()
                .Where(h => h.EntityType == CatalogEntityTypes.Brand
                            && h.ScopeId == CatalogConsts.BrandSlugScope
                            && h.OldSlug == brandSlug)
                .Select(h => (Guid?)h.EntityId)
                .FirstOrDefaultAsync(cancellationToken);

            if (oldBrandId is null)
            {
                return null;
            }

            brand = await db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == oldBrandId, cancellationToken);
            brandIsOld = true;
        }

        if (brand is null)
        {
            return null;
        }

        // (2) CarModel theo (brand_id, Slug); không có thì tra lịch sử Slug của CarModel trong phạm vi Brand này.
        var modelIsOld = false;
        Guid? modelId = await db.CarModels.AsNoTracking()
            .Where(m => m.BrandId == brand.Id && m.Slug == modelSlug)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (modelId is null)
        {
            modelId = await db.SlugHistories.AsNoTracking()
                .Where(h => h.EntityType == CatalogEntityTypes.CarModel
                            && h.ScopeId == brand.Id
                            && h.OldSlug == modelSlug)
                .Select(h => (Guid?)h.EntityId)
                .FirstOrDefaultAsync(cancellationToken);
            modelIsOld = modelId is not null;
        }

        if (modelId is null)
        {
            return null;
        }

        var model = await db.CarModels.AsNoTracking()
            .Include(m => m.Variants)
            .Include(m => m.Images)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == modelId, cancellationToken);

        // Chỉ Published: Draft/Hidden/Archived trả null y như "không tồn tại" để không lộ thông tin (Req 3.8).
        if (model is null || model.Status != CatalogStatus.Published || model.BrandId != brand.Id)
        {
            return null;
        }

        // (3) Một trong hai Slug đã cũ: trả URL hiện tại để BFF chuyển hướng 301 MỘT bước.
        return new SlugResolution(model, brand.Name, brand.Slug, model.Slug, brandIsOld || modelIsOld);
    }

    public virtual async Task<PublicCatalogPage> SearchPublishedAsync(
        PublicCatalogFilter filter, CancellationToken cancellationToken = default)
    {
        var db = await GetDbContextAsync();
        var query = PublishedModels(db);

        if (filter.BrandIds is { Count: > 0 } brandIds)
        {
            query = query.Where(x => brandIds.Contains(x.Model.BrandId));
        }

        var keyword = VietnameseText.Normalize(filter.Keyword);
        if (keyword.Length > 0)
        {
            var pattern = $"%{EscapeLike(keyword)}%";
            query = query.Where(x => EF.Functions.ILike(x.Model.SearchText, pattern, "\\"));
        }

        // Các tiêu chí cấp variant (thân xe, nhiên liệu, hộp số, số chỗ, giá) phải do CÙNG MỘT variant Published thỏa:
        // lọc "dầu + số tự động" không được khớp một model có variant dầu số sàn và variant xăng số tự động.
        // Giá lọc trên TỪNG variant, không lọc trên min/max của model (khoảng lọc có thể nằm giữa hai variant).
        var bodyTypes = filter.BodyTypes is { Count: > 0 } ? filter.BodyTypes : null;
        var fuels = filter.Fuels is { Count: > 0 } ? filter.Fuels : null;
        var transmissions = filter.Transmissions is { Count: > 0 } ? filter.Transmissions : null;
        var seats = filter.Seats is { Count: > 0 } ? filter.Seats : null;
        var minPrice = filter.MinPrice;
        var maxPrice = filter.MaxPrice;

        if (bodyTypes is not null || fuels is not null || transmissions is not null || seats is not null
            || minPrice is not null || maxPrice is not null)
        {
            query = query.Where(x => x.Model.Variants.Any(v =>
                v.Status == CatalogStatus.Published
                && (bodyTypes == null || bodyTypes.Contains(v.BodyType))
                && (fuels == null || fuels.Contains(v.Fuel))
                && (transmissions == null || transmissions.Contains(v.Transmission))
                && (seats == null || seats.Contains(v.Seats))
                && (minPrice == null || (v.ReferencePrice != null && v.ReferencePrice >= minPrice))
                && (maxPrice == null || (v.ReferencePrice != null && v.ReferencePrice <= maxPrice))));
        }

        var total = await query.LongCountAsync(cancellationToken);

        var ordered = filter.Sort switch
        {
            PublicCatalogSort.PriceAsc => query
                .OrderBy(x => x.Model.MinPublishedPrice == null)
                .ThenBy(x => x.Model.MinPublishedPrice)
                .ThenBy(x => x.Model.Slug),
            PublicCatalogSort.PriceDesc => query
                .OrderBy(x => x.Model.MinPublishedPrice == null)
                .ThenByDescending(x => x.Model.MinPublishedPrice)
                .ThenBy(x => x.Model.Slug),
            PublicCatalogSort.NameAsc => query
                .OrderBy(x => x.BrandName.ToLower() + " " + x.Model.Name.ToLower())
                .ThenBy(x => x.Model.Slug),
            _ => query
                .OrderByDescending(x => x.Model.PublishedAt)
                .ThenBy(x => x.Model.Slug),
        };

        var rows = await ProjectRows(ordered.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize))
            .ToListAsync(cancellationToken);

        return new PublicCatalogPage(rows.Select(ToRow).ToList(), total);
    }

    public virtual async Task<IReadOnlyList<PublicCarModelRow>> GetFeaturedPublishedAsync(
        int take, CancellationToken cancellationToken = default)
    {
        var db = await GetDbContextAsync();
        var rows = await ProjectRows(PublishedModels(db)
                .Where(x => x.Model.IsFeatured)
                .OrderBy(x => x.Model.DisplayOrder)
                .ThenByDescending(x => x.Model.PublishedAt)
                .ThenBy(x => x.Model.Slug)
                .Take(take))
            .ToListAsync(cancellationToken);

        return rows.Select(ToRow).ToList();
    }

    public virtual async Task<IReadOnlyList<SitemapEntry>> GetSitemapEntriesAsync(CancellationToken cancellationToken = default)
    {
        var db = await GetDbContextAsync();
        return await PublishedModels(db)
            .OrderBy(x => x.BrandSlug)
            .ThenBy(x => x.Model.Slug)
            .Select(x => new SitemapEntry(
                x.BrandSlug,
                x.Model.Slug,
                x.Model.LastModificationTime ?? x.Model.PublishedAt ?? x.Model.CreationTime))
            .ToListAsync(cancellationToken);
    }

    // ------------------------------------------------------------------ truy vấn dùng chung

    // Lớp (không phải record có tham số hàm tạo) với setter: EF chỉ ghép thành viên để viết tiếp Where/OrderBy lên trên
    // phép chiếu khi dùng khởi tạo theo thuộc tính (MemberInit).
    private sealed class PublishedModel
    {
        public CarModel Model { get; set; } = null!;
        public string BrandName { get; set; } = string.Empty;
        public string BrandSlug { get; set; } = string.Empty;
    }

    /// <summary>CarModel Published có ít nhất một CarVariant Published (Req 2.1), kèm tên và Slug Brand.</summary>
    private static IQueryable<PublishedModel> PublishedModels(CoreServiceDbContext db) =>
        from m in db.CarModels.AsNoTracking()
        join b in db.Brands.AsNoTracking() on m.BrandId equals b.Id
        where m.Status == CatalogStatus.Published
              && m.Variants.Any(v => v.Status == CatalogStatus.Published)
        select new PublishedModel { Model = m, BrandName = b.Name, BrandSlug = b.Slug };

    private sealed record RowData(
        Guid Id, string BrandName, string BrandSlug, string Name, string Slug,
        long? MinPrice, long? MaxPrice, bool IsFeatured, DateTime? PublishedAt,
        string? CoverStorageKey, string? CoverAlt, List<VariantKeySpec> Variants);

    private sealed record VariantKeySpec(string BodyType, string Fuel, string Transmission, int Seats);

    private static IQueryable<RowData> ProjectRows(IQueryable<PublishedModel> source) =>
        source.Select(x => new RowData(
            x.Model.Id,
            x.BrandName,
            x.BrandSlug,
            x.Model.Name,
            x.Model.Slug,
            x.Model.MinPublishedPrice,
            x.Model.MaxPublishedPrice,
            x.Model.IsFeatured,
            x.Model.PublishedAt,
            x.Model.Images.Where(i => i.IsCover).Select(i => i.StorageKey).FirstOrDefault(),
            x.Model.Images.Where(i => i.IsCover).Select(i => i.Alt).FirstOrDefault(),
            x.Model.Variants
                .Where(v => v.Status == CatalogStatus.Published)
                .Select(v => new VariantKeySpec(v.BodyType, v.Fuel, v.Transmission, v.Seats))
                .ToList()));

    private static PublicCarModelRow ToRow(RowData r) => new(
        r.Id,
        r.BrandName,
        r.BrandSlug,
        r.Name,
        r.Slug,
        r.MinPrice,
        r.MaxPrice,
        r.CoverStorageKey,
        r.CoverAlt,
        r.IsFeatured,
        r.PublishedAt,
        r.Variants.Select(v => v.BodyType).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList(),
        r.Variants.Select(v => v.Fuel).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList(),
        r.Variants.Select(v => v.Transmission).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToList(),
        r.Variants.Select(v => v.Seats).Distinct().OrderBy(s => s).ToList());

    /// <summary>Escape <c>\ % _</c> để từ khóa người dùng không thành ký tự đại diện của LIKE/ILIKE.</summary>
    internal static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
