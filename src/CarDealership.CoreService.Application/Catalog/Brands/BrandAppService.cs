using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Audit;
using CarDealership.CoreService.Catalog.Slugs;
using CarDealership.CoreService.Identity.Permissions;
using CarDealership.CoreService.Text;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace CarDealership.CoreService.Catalog.Brands;

/// <summary>
/// Quản trị Brand (Req 7). Mọi thao tác cần <c>Catalog.Edit</c>. Mỗi thay đổi ghi ĐÚNG MỘT Audit_Log trong cùng UoW.
/// Insert/Update dùng <c>autoSave: true</c> để vi phạm UNIQUE của DB (tên/Slug trùng, kể cả đồng thời) nổ ra ngay tại đây
/// dưới dạng 409, TRƯỚC khi ghi audit.
/// </summary>
[Authorize(AppPermissions.CatalogEdit)]
public class BrandAppService : CoreServiceAppService, IBrandAppService
{
    private readonly IRepository<Brand, Guid> _brands;
    private readonly IAuditTrail _audit;
    private readonly SlugChangeManager _slugChanges;
    private readonly BrandStatusManager _statusManager;

    public BrandAppService(
        IRepository<Brand, Guid> brands,
        IAuditTrail audit,
        SlugChangeManager slugChanges,
        BrandStatusManager statusManager)
    {
        _brands = brands;
        _audit = audit;
        _slugChanges = slugChanges;
        _statusManager = statusManager;
    }

    public virtual async Task<PagedResultDto<BrandDto>> GetListAsync(BrandListInput input)
    {
        var (skip, take) = CatalogPaging.Validate(input.Page, input.PageSize);

        var all = await _brands.GetListAsync();

        // Brand ít (vài chục): lọc trong bộ nhớ để dùng chung VietnameseText.Normalize (tìm không dấu) với phần còn lại.
        var keyword = VietnameseText.Normalize(input.Keyword);
        var filtered = all
            .Where(b => input.Status is null || b.Status == input.Status)
            .Where(b => keyword.Length == 0 || VietnameseText.Normalize(b.Name).Contains(keyword, StringComparison.Ordinal))
            .OrderBy(b => b.DisplayOrder)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new PagedResultDto<BrandDto>(
            filtered.Count,
            filtered.Skip(skip).Take(take).Select(ToDto).ToList());
    }

    public virtual async Task<BrandDto> GetAsync(Guid id) => ToDto(await _brands.GetAsync(id));

    public virtual async Task<BrandDto> CreateAsync(SaveBrandInput input)
    {
        var brand = new Brand(
            GuidGenerator.Create(),
            input.Name,
            input.Slug,
            input.Status ?? BrandStatus.Active,
            input.DisplayOrder,
            input.LogoKey);

        await _slugChanges.EnsureAvailableAsync(CatalogEntityTypes.Brand, brand.Id, CatalogConsts.BrandSlugScope, brand.Slug);
        await _brands.InsertAsync(brand, autoSave: true);

        await _audit.WriteAsync(AuditActions.Create, CatalogEntityTypes.Brand, brand.Id.ToString(), null, Snapshot(brand));
        return ToDto(brand);
    }

    public virtual async Task<BrandDto> UpdateAsync(Guid id, SaveBrandInput input)
    {
        var brand = await _brands.GetAsync(id);
        var before = Snapshot(brand);
        var oldSlug = brand.Slug;

        ApplyConcurrencyStamp(brand, input.ConcurrencyStamp);
        brand.Update(input.Name, input.Slug, input.DisplayOrder, input.LogoKey);

        var slugChanged = !string.Equals(oldSlug, brand.Slug, StringComparison.Ordinal);
        if (slugChanged)
        {
            await _slugChanges.EnsureAvailableAsync(CatalogEntityTypes.Brand, brand.Id, CatalogConsts.BrandSlugScope, brand.Slug);
        }

        await _brands.UpdateAsync(brand, autoSave: true);

        if (slugChanged)
        {
            await _slugChanges.RecordChangeAsync(
                CatalogEntityTypes.Brand, brand.Id, CatalogConsts.BrandSlugScope, oldSlug, brand.Slug, Clock.Now);
        }

        await _audit.WriteAsync(AuditActions.Update, CatalogEntityTypes.Brand, brand.Id.ToString(), before, Snapshot(brand));
        return ToDto(brand);
    }

    public virtual async Task<BrandDto> ChangeStatusAsync(Guid id, ChangeBrandStatusInput input)
    {
        var brand = await _brands.GetAsync(id);
        var before = Snapshot(brand);

        ApplyConcurrencyStamp(brand, input.ConcurrencyStamp);
        await _statusManager.ChangeStatusAsync(brand, input.Status);
        await _brands.UpdateAsync(brand, autoSave: true);

        await _audit.WriteAsync(AuditActions.ChangeStatus, CatalogEntityTypes.Brand, brand.Id.ToString(), before, Snapshot(brand));
        return ToDto(brand);
    }

    /// <summary>
    /// Có gửi stamp thì gán vào entity ĐÃ LOAD: ABP lấy giá trị này làm giá trị gốc khi so khớp ở UPDATE, nên lệch phiên bản
    /// thì EF ném lỗi xung đột (409). Quên gán bước này thì EF so với stamp vừa đọc và không bao giờ 409.
    /// </summary>
    private static void ApplyConcurrencyStamp(IHasConcurrencyStamp entity, string? stamp)
    {
        if (!string.IsNullOrEmpty(stamp))
        {
            entity.ConcurrencyStamp = stamp;
        }
    }

    internal static IReadOnlyDictionary<string, object?> Snapshot(Brand brand) => new Dictionary<string, object?>
    {
        ["name"] = brand.Name,
        ["slug"] = brand.Slug,
        ["status"] = brand.Status,
        ["displayOrder"] = brand.DisplayOrder,
        ["logoKey"] = brand.LogoKey,
    };

    internal static BrandDto ToDto(Brand brand) => new()
    {
        Id = brand.Id,
        Name = brand.Name,
        Slug = brand.Slug,
        Status = brand.Status,
        DisplayOrder = brand.DisplayOrder,
        LogoKey = brand.LogoKey,
        IsDemo = brand.IsDemo,
        ConcurrencyStamp = brand.ConcurrencyStamp,
        CreationTime = brand.CreationTime,
        LastModificationTime = brand.LastModificationTime,
    };
}
