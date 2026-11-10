using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Audit;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Identity.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace CarDealership.CoreService.Catalog.CarModels;

/// <summary>
/// CarVariant ĐI QUA aggregate CarModel (load root, gọi method của root, lưu root): không có repository riêng cho variant.
/// Sau mỗi thay đổi tính lại khoảng giá và search_text của model. Mỗi thay đổi ghi đúng một Audit_Log (thực thể CarVariant).
/// </summary>
public class CarVariantAppService : CoreServiceAppService, ICarVariantAppService
{
    private readonly ICarModelRepository _carModels;
    private readonly IRepository<Brand, Guid> _brands;
    private readonly IAuditTrail _audit;

    public CarVariantAppService(ICarModelRepository carModels, IRepository<Brand, Guid> brands, IAuditTrail audit)
    {
        _carModels = carModels;
        _brands = brands;
        _audit = audit;
    }

    [Authorize(AppPermissions.CatalogEdit)]
    public virtual async Task<List<CarVariantDto>> GetListAsync(Guid carModelId)
    {
        var model = await LoadAsync(carModelId);
        return model.Variants.OrderBy(v => v.Name).Select(CatalogMapping.ToDto).ToList();
    }

    [Authorize(AppPermissions.CatalogEdit)]
    public virtual async Task<CarVariantDto> GetAsync(Guid carModelId, Guid variantId) =>
        CatalogMapping.ToDto((await LoadAsync(carModelId)).GetVariant(variantId));

    [Authorize(AppPermissions.CatalogEdit)]
    public virtual async Task<CarVariantDto> CreateAsync(Guid carModelId, SaveCarVariantInput input)
    {
        var model = await LoadAsync(carModelId);
        var brand = await _brands.GetAsync(model.BrandId);

        var variant = model.AddVariant(GuidGenerator.Create(), CatalogMapping.ToData(input));
        model.RefreshDerived(brand.Name);
        await _carModels.UpdateAsync(model, autoSave: true);

        await _audit.WriteAsync(AuditActions.Create, CatalogEntityTypes.CarVariant, variant.Id.ToString(), null, CatalogMapping.Snapshot(variant));
        return CatalogMapping.ToDto(variant);
    }

    [Authorize(AppPermissions.CatalogEdit)]
    public virtual async Task<CarVariantDto> UpdateAsync(Guid carModelId, Guid variantId, SaveCarVariantInput input)
    {
        var model = await LoadAsync(carModelId);
        var brand = await _brands.GetAsync(model.BrandId);
        var variant = model.GetVariant(variantId);
        var before = CatalogMapping.Snapshot(variant);

        CarModelAppService.ApplyConcurrencyStamp(variant, input.ConcurrencyStamp);
        model.UpdateVariant(variantId, CatalogMapping.ToData(input));
        model.RefreshDerived(brand.Name);
        await _carModels.UpdateAsync(model, autoSave: true);

        await _audit.WriteAsync(AuditActions.Update, CatalogEntityTypes.CarVariant, variant.Id.ToString(), before, CatalogMapping.Snapshot(variant));
        return CatalogMapping.ToDto(variant);
    }

    [Authorize(AppPermissions.CatalogPublish)]
    public virtual async Task<CarVariantDto> ChangeStatusAsync(Guid carModelId, Guid variantId, ChangeCatalogStatusInput input)
    {
        var model = await LoadAsync(carModelId);
        var brand = await _brands.GetAsync(model.BrandId);
        var variant = model.GetVariant(variantId);
        var before = CatalogMapping.Snapshot(variant);

        CarModelAppService.ApplyConcurrencyStamp(variant, input.ConcurrencyStamp);
        model.ChangeVariantStatus(variantId, input.Status);
        model.RefreshDerived(brand.Name);
        await _carModels.UpdateAsync(model, autoSave: true);

        await _audit.WriteAsync(AuditActions.ChangeStatus, CatalogEntityTypes.CarVariant, variant.Id.ToString(), before, CatalogMapping.Snapshot(variant));
        return CatalogMapping.ToDto(variant);
    }

    private async Task<CarModel> LoadAsync(Guid id) =>
        await _carModels.FindWithDetailsAsync(id) ?? throw new EntityNotFoundException(typeof(CarModel), id);
}
