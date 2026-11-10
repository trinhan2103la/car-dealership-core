using System.Collections.Generic;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.Public;
using CarDealership.CoreService.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace CarDealership.CoreService.Catalog;

/// <summary>
/// Danh mục công khai cho BFF (Public_Website). Vẫn cần Edge key + Service_Credential scope=bff (fallback policy) nhưng không
/// cần phiên Admin. Chỉ dữ liệu Published. 400 tham số sai (nêu tên từng tham số), 404 không tồn tại / không Published.
/// </summary>
[Route("api/catalog/public")]
public class PublicCatalogController : CoreServiceController
{
    private readonly IPublicCatalogAppService _catalog;

    public PublicCatalogController(IPublicCatalogAppService catalog)
    {
        _catalog = catalog;
    }

    [HttpGet("car-models")]
    public virtual Task<PublicCarListDto> GetCarModelsAsync([FromQuery] PublicCarModelListInput input) =>
        _catalog.GetCarModelsAsync(input);

    /// <summary>200 với <c>carModel</c> (Slug hiện tại) hoặc <c>redirect</c> (Slug cũ: BFF trả 301 một bước); 404 nếu không có.</summary>
    [HttpGet("car-models/by-slug")]
    public virtual Task<PublicCarModelLookupDto> GetBySlugsAsync([FromQuery] string? brandSlug, [FromQuery] string? modelSlug) =>
        _catalog.GetCarModelBySlugsAsync(brandSlug, modelSlug);

    [HttpGet("home-cars")]
    public virtual Task<List<PublicCarCardDto>> GetHomeCarsAsync() => _catalog.GetHomeCarsAsync();

    [HttpGet("brands")]
    public virtual Task<List<PublicBrandDto>> GetBrandsAsync() => _catalog.GetBrandsAsync();

    [HttpGet("sitemap")]
    public virtual Task<List<SitemapEntryDto>> GetSitemapAsync() => _catalog.GetSitemapAsync();
}
