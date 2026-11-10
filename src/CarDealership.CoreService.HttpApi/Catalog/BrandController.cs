using System;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Controllers;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;

namespace CarDealership.CoreService.Catalog;

/// <summary>
/// Quản trị Brand (chỉ BFF gọi, Service_Credential scope=bff). Quyền <c>Catalog.Edit</c> kiểm ở
/// <see cref="BrandAppService"/>:
/// 400 lỗi theo trường, 403 thiếu quyền, 404 không tồn tại, 409 trùng tên/Slug hoặc lệch phiên bản,
/// 422 chuyển Inactive khi còn CarModel Published.
/// </summary>
[Route("api/catalog/brands")]
public class BrandController : CoreServiceController
{
    private readonly IBrandAppService _brands;

    public BrandController(IBrandAppService brands)
    {
        _brands = brands;
    }

    [HttpGet]
    public virtual Task<PagedResultDto<BrandDto>> GetListAsync([FromQuery] BrandListInput input) => _brands.GetListAsync(input);

    [HttpGet("{id:guid}")]
    public virtual Task<BrandDto> GetAsync(Guid id) => _brands.GetAsync(id);

    [HttpPost]
    public virtual Task<BrandDto> CreateAsync([FromBody] SaveBrandInput input) => _brands.CreateAsync(input);

    [HttpPut("{id:guid}")]
    public virtual Task<BrandDto> UpdateAsync(Guid id, [FromBody] SaveBrandInput input) => _brands.UpdateAsync(id, input);

    [HttpPost("{id:guid}/status")]
    public virtual Task<BrandDto> ChangeStatusAsync(Guid id, [FromBody] ChangeBrandStatusInput input) =>
        _brands.ChangeStatusAsync(id, input);
}
