using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CarDealership.CoreService.Catalog.CarModels;
using CarDealership.CoreService.Controllers;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;

namespace CarDealership.CoreService.Catalog;

/// <summary>
/// Quản trị CarModel (BFF gọi). Quyền kiểm ở app service: nội dung <c>Catalog.Edit</c>, chuyển trạng thái <c>Catalog.Publish</c>.
/// 400 lỗi theo trường; 403; 404; 409 trùng tên/Slug trong Brand hoặc lệch phiên bản; 422 chuyển trạng thái/Publish_Validation.
/// </summary>
[Route("api/catalog/car-models")]
public class CarModelController : CoreServiceController
{
    private readonly ICarModelAppService _carModels;

    public CarModelController(ICarModelAppService carModels)
    {
        _carModels = carModels;
    }

    [HttpGet]
    public virtual Task<PagedResultDto<CarModelSummaryDto>> GetListAsync([FromQuery] CarModelListInput input) =>
        _carModels.GetListAsync(input);

    [HttpGet("{id:guid}")]
    public virtual Task<CarModelDto> GetAsync(Guid id) => _carModels.GetAsync(id);

    [HttpPost]
    public virtual Task<CarModelDto> CreateAsync([FromBody] CreateCarModelInput input) => _carModels.CreateAsync(input);

    [HttpPut("{id:guid}")]
    public virtual Task<CarModelDto> UpdateAsync(Guid id, [FromBody] UpdateCarModelInput input) => _carModels.UpdateAsync(id, input);

    [HttpPost("{id:guid}/transitions")]
    public virtual Task<CarModelDto> ChangeStatusAsync(Guid id, [FromBody] ChangeCatalogStatusInput input) =>
        _carModels.ChangeStatusAsync(id, input);

    [HttpGet("{id:guid}/publish-check")]
    public virtual Task<PublishCheckDto> GetPublishCheckAsync(Guid id) => _carModels.GetPublishCheckAsync(id);
}

/// <summary>CarVariant lồng dưới CarModel (chỉ truy cập qua aggregate). Cùng quy tắc quyền và mã lỗi như CarModel.</summary>
[Route("api/catalog/car-models/{carModelId:guid}/variants")]
public class CarVariantController : CoreServiceController
{
    private readonly ICarVariantAppService _variants;

    public CarVariantController(ICarVariantAppService variants)
    {
        _variants = variants;
    }

    [HttpGet]
    public virtual Task<List<CarVariantDto>> GetListAsync(Guid carModelId) => _variants.GetListAsync(carModelId);

    [HttpGet("{variantId:guid}")]
    public virtual Task<CarVariantDto> GetAsync(Guid carModelId, Guid variantId) => _variants.GetAsync(carModelId, variantId);

    [HttpPost]
    public virtual Task<CarVariantDto> CreateAsync(Guid carModelId, [FromBody] SaveCarVariantInput input) =>
        _variants.CreateAsync(carModelId, input);

    [HttpPut("{variantId:guid}")]
    public virtual Task<CarVariantDto> UpdateAsync(Guid carModelId, Guid variantId, [FromBody] SaveCarVariantInput input) =>
        _variants.UpdateAsync(carModelId, variantId, input);

    [HttpPost("{variantId:guid}/transitions")]
    public virtual Task<CarVariantDto> ChangeStatusAsync(Guid carModelId, Guid variantId, [FromBody] ChangeCatalogStatusInput input) =>
        _variants.ChangeStatusAsync(carModelId, variantId, input);
}
