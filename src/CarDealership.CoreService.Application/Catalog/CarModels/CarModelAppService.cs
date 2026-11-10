using System;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Audit;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Catalog.Publishing;
using CarDealership.CoreService.Catalog.Slugs;
using CarDealership.CoreService.Identity.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace CarDealership.CoreService.Catalog.CarModels;

/// <summary>
/// Quản trị CarModel (Req 8, 10). Quyền đặt THEO TỪNG METHOD: sửa nội dung cần <c>Catalog.Edit</c>, chuyển trạng thái chỉ cần
/// <c>Catalog.Publish</c> (thiếu thì 403 và không chạy Publish_Validation, Req 10.8). Mỗi thay đổi ghi đúng một Audit_Log trong
/// cùng UoW. Insert/Update dùng <c>autoSave</c> để vi phạm UNIQUE nổ ra thành 409 trước khi ghi audit.
/// </summary>
public class CarModelAppService : CoreServiceAppService, ICarModelAppService
{
    private readonly ICarModelRepository _carModels;
    private readonly IRepository<Brand, Guid> _brands;
    private readonly IAuditTrail _audit;
    private readonly SlugChangeManager _slugChanges;

    public CarModelAppService(
        ICarModelRepository carModels,
        IRepository<Brand, Guid> brands,
        IAuditTrail audit,
        SlugChangeManager slugChanges)
    {
        _carModels = carModels;
        _brands = brands;
        _audit = audit;
        _slugChanges = slugChanges;
    }

    [Authorize(AppPermissions.CatalogEdit)]
    public virtual async Task<PagedResultDto<CarModelSummaryDto>> GetListAsync(CarModelListInput input)
    {
        var (skip, take) = CatalogPaging.Validate(input.Page, input.PageSize);
        var (items, total) = await _carModels.GetAdminListAsync(
            new AdminCarModelFilter(input.BrandId, input.Status, input.Keyword, skip, take));

        return new PagedResultDto<CarModelSummaryDto>(total, items.Select(CatalogMapping.ToSummaryDto).ToList());
    }

    [Authorize(AppPermissions.CatalogEdit)]
    public virtual async Task<CarModelDto> GetAsync(Guid id) => CatalogMapping.ToDto(await LoadAsync(id));

    [Authorize(AppPermissions.CatalogEdit)]
    public virtual async Task<CarModelDto> CreateAsync(CreateCarModelInput input)
    {
        var errors = CarModel.Validate(input.Name, input.Slug, input.Description, input.DisplayOrder);
        var brand = input.BrandId == Guid.Empty ? null : await _brands.FindAsync(input.BrandId);
        if (brand is null)
        {
            // Brand phải tồn tại (Active hoặc Inactive, Req 8.2): lỗi của trường brandId, gom cùng các lỗi khác.
            errors.Add("brandId", "Brand không tồn tại.");
        }

        errors.ThrowIfAny();

        var model = new CarModel(
            GuidGenerator.Create(),
            brand!.Id,
            brand.Name,
            input.Name,
            input.Slug,
            input.Description,
            input.IsFeatured,
            input.DisplayOrder);

        await _slugChanges.EnsureAvailableAsync(CatalogEntityTypes.CarModel, model.Id, brand.Id, model.Slug);
        await _carModels.InsertAsync(model, autoSave: true);

        await _audit.WriteAsync(AuditActions.Create, CatalogEntityTypes.CarModel, model.Id.ToString(), null, CatalogMapping.Snapshot(model));
        return CatalogMapping.ToDto(model);
    }

    [Authorize(AppPermissions.CatalogEdit)]
    public virtual async Task<CarModelDto> UpdateAsync(Guid id, UpdateCarModelInput input)
    {
        var model = await LoadAsync(id);
        var brand = await _brands.GetAsync(model.BrandId);
        var before = CatalogMapping.Snapshot(model);

        ApplyConcurrencyStamp(model, input.ConcurrencyStamp);
        var oldSlug = model.Update(brand.Name, brand.Status, input.Name, input.Slug, input.Description, input.IsFeatured, input.DisplayOrder);

        if (oldSlug is not null)
        {
            await _slugChanges.EnsureAvailableAsync(CatalogEntityTypes.CarModel, model.Id, model.BrandId, model.Slug);
        }

        await _carModels.UpdateAsync(model, autoSave: true);

        // Chỉ CarModel ĐÃ TỪNG Published mới có URL công khai cần chuyển hướng (Req 8.13).
        if (oldSlug is not null && model.HasEverBeenPublished)
        {
            await _slugChanges.RecordChangeAsync(CatalogEntityTypes.CarModel, model.Id, model.BrandId, oldSlug, model.Slug, Clock.Now);
        }

        await _audit.WriteAsync(AuditActions.Update, CatalogEntityTypes.CarModel, model.Id.ToString(), before, CatalogMapping.Snapshot(model));
        return CatalogMapping.ToDto(model);
    }

    [Authorize(AppPermissions.CatalogPublish)]
    public virtual async Task<CarModelDto> ChangeStatusAsync(Guid id, ChangeCatalogStatusInput input)
    {
        var model = await LoadAsync(id);
        var brand = await _brands.GetAsync(model.BrandId);
        var before = CatalogMapping.Snapshot(model);

        ApplyConcurrencyStamp(model, input.ConcurrencyStamp);
        model.ChangeStatus(input.Status, brand.Status, Clock.Now);
        await _carModels.UpdateAsync(model, autoSave: true);

        await _audit.WriteAsync(AuditActions.ChangeStatus, CatalogEntityTypes.CarModel, model.Id.ToString(), before, CatalogMapping.Snapshot(model));
        return CatalogMapping.ToDto(model);
    }

    [Authorize(AppPermissions.CatalogEdit)]
    public virtual async Task<PublishCheckDto> GetPublishCheckAsync(Guid id)
    {
        var model = await LoadAsync(id);
        var brand = await _brands.GetAsync(model.BrandId);
        var result = model.CheckPublishable(brand.Status);

        return new PublishCheckDto
        {
            CanPublish = result.IsValid,
            Rules = PublishValidator.CarModelRuleCodes
                .Select(code => new PublishRuleResultDto { Code = code, Passed = !result.Violations.Contains(code) })
                .ToList(),
            InvalidImageIds = result.InvalidImageIds.ToList(),
        };
    }

    private async Task<CarModel> LoadAsync(Guid id) =>
        await _carModels.FindWithDetailsAsync(id) ?? throw new EntityNotFoundException(typeof(CarModel), id);

    /// <summary>Gán stamp client gửi vào entity đã load để EF so khớp đúng phiên bản (lệch → 409).</summary>
    internal static void ApplyConcurrencyStamp(IHasConcurrencyStamp entity, string? stamp)
    {
        if (!string.IsNullOrEmpty(stamp))
        {
            entity.ConcurrencyStamp = stamp;
        }
    }
}
