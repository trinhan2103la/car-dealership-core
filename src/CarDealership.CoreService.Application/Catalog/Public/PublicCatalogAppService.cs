using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Catalog.Slugs;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace CarDealership.CoreService.Catalog.Public;

/// <summary>
/// Danh mục công khai (Req 1, 2, 3). Không có [Authorize] của ABP: không cần phiên Admin, nhưng request vẫn phải qua Edge key +
/// Service_Credential (fallback policy ServiceBff ở Host). Chỉ trả dữ liệu Published; mọi trạng thái khác giống "không tồn tại".
/// </summary>
public class PublicCatalogAppService : CoreServiceAppService, IPublicCatalogAppService
{
    public const int HomeFeaturedMax = 8;

    private static readonly IReadOnlyDictionary<string, PublicCatalogSort> Sorts =
        new Dictionary<string, PublicCatalogSort>(StringComparer.OrdinalIgnoreCase)
        {
            ["newest"] = PublicCatalogSort.Newest,
            ["price_asc"] = PublicCatalogSort.PriceAsc,
            ["price_desc"] = PublicCatalogSort.PriceDesc,
            ["name_asc"] = PublicCatalogSort.NameAsc,
        };

    private readonly ICarModelRepository _carModels;
    private readonly IRepository<Brand, Guid> _brands;

    public PublicCatalogAppService(ICarModelRepository carModels, IRepository<Brand, Guid> brands)
    {
        _carModels = carModels;
        _brands = brands;
    }

    public virtual async Task<PublicCarListDto> GetCarModelsAsync(PublicCarModelListInput input)
    {
        var filter = await ValidateAsync(input);
        var page = await _carModels.SearchPublishedAsync(filter);

        return new PublicCarListDto
        {
            Items = page.Items.Select(ToCard).ToList(),
            TotalItems = page.TotalItems,
            TotalPages = (int)((page.TotalItems + filter.PageSize - 1) / filter.PageSize),
            Page = filter.Page,
            PageSize = filter.PageSize,
        };
    }

    public virtual async Task<PublicCarModelLookupDto> GetCarModelBySlugsAsync(string? brandSlug, string? modelSlug)
    {
        // Slug sai định dạng không thể tồn tại: 404 như mọi trường hợp không tìm thấy (không tiết lộ gì).
        if (!Slug.IsValid(brandSlug) || !Slug.IsValid(modelSlug))
        {
            throw new EntityNotFoundException(typeof(CarModel));
        }

        var resolution = await _carModels.FindPublishedBySlugsAsync(brandSlug!, modelSlug!)
                         ?? throw new EntityNotFoundException(typeof(CarModel));

        if (resolution.IsRedirect)
        {
            return new PublicCarModelLookupDto { Redirect = $"/xe/{resolution.BrandSlug}/{resolution.ModelSlug}" };
        }

        return new PublicCarModelLookupDto { CarModel = ToDetail(resolution) };
    }

    public virtual async Task<List<PublicCarCardDto>> GetHomeCarsAsync() =>
        (await _carModels.GetFeaturedPublishedAsync(HomeFeaturedMax)).Select(ToCard).ToList();

    public virtual async Task<List<PublicBrandDto>> GetBrandsAsync()
    {
        var brands = await _brands.GetListAsync(b => b.Status == BrandStatus.Active);
        return brands
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .Select(b => new PublicBrandDto { Id = b.Id, Name = b.Name, Slug = b.Slug, LogoKey = b.LogoKey, DisplayOrder = b.DisplayOrder })
            .ToList();
    }

    public virtual async Task<List<SitemapEntryDto>> GetSitemapAsync() =>
        (await _carModels.GetSitemapEntriesAsync())
        .Select(e => new SitemapEntryDto { Path = $"/xe/{e.BrandSlug}/{e.ModelSlug}", LastModified = e.LastModifiedUtc })
        .ToList();

    /// <summary>Kiểm MỌI tham số và báo lỗi theo tên tham số trong một phản hồi 400 (Req 2.17).</summary>
    private async Task<PublicCatalogFilter> ValidateAsync(PublicCarModelListInput input)
    {
        var errors = new FieldErrors();

        if (input.Page < 1)
        {
            errors.Add("page", "Số trang bắt đầu từ 1.");
        }

        if (input.PageSize is < 1 or > CatalogConsts.PublicPageSizeMax)
        {
            errors.Add("pageSize", $"Kích thước trang phải từ 1 đến {CatalogConsts.PublicPageSizeMax}.");
        }

        var sort = PublicCatalogSort.Newest;
        if (!string.IsNullOrWhiteSpace(input.Sort) && !Sorts.TryGetValue(input.Sort.Trim(), out sort))
        {
            errors.Add("sort", "Tiêu chí sắp xếp không được hỗ trợ.");
        }

        var keyword = input.Q?.Trim();
        if (keyword is { Length: > CatalogConsts.SearchKeywordMaxLength })
        {
            errors.Add("q", $"Từ khóa dài tối đa {CatalogConsts.SearchKeywordMaxLength} ký tự.");
        }

        if (input.MinPrice is < 0)
        {
            errors.Add("minPrice", "Giá không được âm.");
        }

        if (input.MaxPrice is < 0)
        {
            errors.Add("maxPrice", "Giá không được âm.");
        }

        if (input.MinPrice is { } min && input.MaxPrice is { } max && min > max)
        {
            errors.Add("minPrice", "Giá tối thiểu không được lớn hơn giá tối đa.");
        }

        if (input.Seats?.Any(s => s is < CatalogConsts.SeatsMin or > CatalogConsts.SeatsMax) == true)
        {
            errors.Add("seats", $"Số chỗ phải từ {CatalogConsts.SeatsMin} đến {CatalogConsts.SeatsMax}.");
        }

        // Giá trị bộ lọc Brand phải tồn tại: cần DB nên chỉ Core_API kiểm được (Req 2.17).
        List<Guid>? brandIds = null;
        var brandSlugs = Clean(input.Brand)?.Select(Slug.Normalize).Distinct().ToList();
        if (brandSlugs is { Count: > 0 })
        {
            var found = await _brands.GetListAsync(b => brandSlugs.Contains(b.Slug));
            if (found.Count != brandSlugs.Count)
            {
                errors.Add("brand", "Có Brand không tồn tại.");
            }

            brandIds = found.Select(b => b.Id).ToList();
        }

        errors.ThrowIfAny();

        return new PublicCatalogFilter(
            string.IsNullOrEmpty(keyword) ? null : keyword,
            brandIds,
            Clean(input.BodyType),
            Clean(input.Fuel),
            Clean(input.Transmission),
            input.Seats is { Count: > 0 } ? input.Seats.Distinct().ToList() : null,
            input.MinPrice,
            input.MaxPrice,
            sort,
            input.Page,
            input.PageSize);
    }

    private static List<string>? Clean(List<string>? values)
    {
        var cleaned = values?.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct().ToList();
        return cleaned is { Count: > 0 } ? cleaned : null;
    }

    private static PublicCarCardDto ToCard(PublicCarModelRow r) => new()
    {
        Id = r.Id,
        BrandName = r.BrandName,
        BrandSlug = r.BrandSlug,
        Name = r.Name,
        Slug = r.Slug,
        MinPrice = r.MinPrice,
        MaxPrice = r.MaxPrice,
        CoverStorageKey = r.CoverStorageKey,
        CoverAlt = r.CoverAlt,
        IsFeatured = r.IsFeatured,
        PublishedAt = r.PublishedAt,
        BodyTypes = r.BodyTypes.ToList(),
        Fuels = r.Fuels.ToList(),
        Transmissions = r.Transmissions.ToList(),
        Seats = r.Seats.ToList(),
    };

    private static PublicCarModelDetailDto ToDetail(SlugResolution resolution)
    {
        var model = resolution.CarModel;
        return new PublicCarModelDetailDto
        {
            Id = model.Id,
            BrandName = resolution.BrandName,
            BrandSlug = resolution.BrandSlug,
            Name = model.Name,
            Slug = model.Slug,
            Description = model.Description,
            MinPrice = model.MinPublishedPrice,
            MaxPrice = model.MaxPublishedPrice,
            Images = model.Images
                .OrderByDescending(i => i.IsCover)
                .ThenBy(i => i.DisplayOrder)
                .Select(i => new PublicImageDto { StorageKey = i.StorageKey, Alt = i.Alt, IsCover = i.IsCover })
                .ToList(),
            Variants = model.Variants
                .Where(v => v.Status == CatalogStatus.Published)
                .OrderBy(v => v.ReferencePrice ?? long.MaxValue)
                .ThenBy(v => v.Name, StringComparer.Ordinal)
                .Select(v => new PublicCarVariantDto
                {
                    Id = v.Id,
                    Name = v.Name,
                    ReferencePrice = v.ReferencePrice,
                    ContactForPrice = v.ContactForPrice,
                    BodyType = v.BodyType,
                    Fuel = v.Fuel,
                    Transmission = v.Transmission,
                    Seats = v.Seats,
                    Engine = v.Engine,
                    Power = v.Power,
                    ExtraSpecGroups = v.ExtraSpecs
                        .Where(e => !string.IsNullOrWhiteSpace(e.Value))
                        .GroupBy(e => e.Group)
                        .Select(g => new PublicExtraSpecGroupDto
                        {
                            Group = g.Key,
                            Items = g.Select(e => new PublicExtraSpecDto { Name = e.Name, Value = e.Value, Unit = e.Unit }).ToList(),
                        })
                        .ToList(),
                })
                .ToList(),
        };
    }
}
