using System;
using CarDealership.CoreService.Catalog.Slugs;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;

namespace CarDealership.CoreService.Catalog.Brands;

/// <summary>
/// Aggregate root Brand (bảng <c>cat_brands</c>, Req 7). Là aggregate riêng: CarModel chỉ giữ <see cref="Entity{TKey}.Id"/>
/// của Brand, không có navigation sang Brand. Tên và Slug duy nhất do UNIQUE index của DB quyết định (409 kể cả đồng thời);
/// domain chỉ kiểm định dạng. <see cref="AuditedAggregateRoot{TKey}"/> cho metadata ABP và <c>ConcurrencyStamp</c>
/// (khác Audit_Log nghiệp vụ). Không dùng soft delete: Inactive là trạng thái nghiệp vụ.
/// </summary>
public class Brand : AuditedAggregateRoot<Guid>
{
    /// <summary>Tên đã trim, 1–100 ký tự.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Slug đã chuẩn hóa (chữ thường), duy nhất toàn hệ thống.</summary>
    public string Slug { get; private set; } = string.Empty;

    public BrandStatus Status { get; private set; }

    public int DisplayOrder { get; private set; }

    /// <summary>Khóa object trong Object_Storage của logo (tùy chọn).</summary>
    public string? LogoKey { get; private set; }

    public bool IsDemo { get; private set; }

    private Brand()
    {
        // EF Core
    }

    public Brand(
        Guid id,
        string? name,
        string? slug,
        BrandStatus status = BrandStatus.Active,
        int displayOrder = 0,
        string? logoKey = null,
        bool isDemo = false) : base(id)
    {
        var errors = Validate(name, slug, displayOrder, logoKey);
        errors.ThrowIfAny();

        Name = name!.Trim();
        Slug = Slugs.Slug.Normalize(slug);
        Status = status;
        DisplayOrder = displayOrder;
        LogoKey = NormalizeOptional(logoKey);
        IsDemo = isDemo;
    }

    /// <summary>Sửa thông tin. Đổi tên phát <see cref="BrandRenamedEvent"/> (cập nhật search_text của CarModel, cùng UoW).</summary>
    public void Update(string? name, string? slug, int displayOrder, string? logoKey)
    {
        Validate(name, slug, displayOrder, logoKey).ThrowIfAny();

        var newName = name!.Trim();
        if (!string.Equals(newName, Name, StringComparison.Ordinal))
        {
            AddLocalEvent(new BrandRenamedEvent(Id, Name, newName));
        }

        Name = newName;
        Slug = Slugs.Slug.Normalize(slug);
        DisplayOrder = displayOrder;
        LogoKey = NormalizeOptional(logoKey);
    }

    /// <summary>
    /// Đặt trạng thái. KHÔNG tự kiểm "còn CarModel Published" (cần repository CarModel): luôn đi qua
    /// <see cref="BrandStatusManager"/> khi chuyển sang Inactive.
    /// </summary>
    public void SetStatus(BrandStatus status)
    {
        Status = status;
    }

    /// <summary>Kiểm định dạng; trả lỗi theo trường để người gọi gom hoặc ném.</summary>
    public static FieldErrors Validate(string? name, string? slug, int displayOrder, string? logoKey)
    {
        var errors = new FieldErrors();
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            errors.Add("name", "Tên không được rỗng.");
        }
        else if (trimmed.Length > CatalogConsts.NameMaxLength)
        {
            errors.Add("name", $"Tên dài tối đa {CatalogConsts.NameMaxLength} ký tự.");
        }

        var slugError = Slugs.Slug.GetError(slug);
        if (slugError is not null)
        {
            errors.Add("slug", slugError);
        }

        if (displayOrder is < CatalogConsts.DisplayOrderMin or > CatalogConsts.DisplayOrderMax)
        {
            errors.Add("displayOrder", $"Thứ tự hiển thị phải từ {CatalogConsts.DisplayOrderMin} đến {CatalogConsts.DisplayOrderMax}.");
        }

        if (logoKey is { Length: > CatalogConsts.LogoKeyMaxLength })
        {
            errors.Add("logoKey", $"Khóa logo dài tối đa {CatalogConsts.LogoKeyMaxLength} ký tự.");
        }

        return errors;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

/// <summary>Brand đổi tên. Handler cập nhật <c>search_text</c> của mọi CarModel thuộc Brand trong cùng UoW.</summary>
public sealed record BrandRenamedEvent(Guid BrandId, string OldName, string NewName);
