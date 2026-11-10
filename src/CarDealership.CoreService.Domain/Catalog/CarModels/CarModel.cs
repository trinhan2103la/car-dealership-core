using System;
using System.Collections.Generic;
using System.Linq;
using CarDealership.CoreService.Catalog.Publishing;
using CarDealership.CoreService.Catalog.Slugs;
using CarDealership.CoreService.Text;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;

namespace CarDealership.CoreService.Catalog.CarModels;

/// <summary>
/// Aggregate root CarModel (bảng <c>cat_car_models</c>, Req 8, 9, 10). Chứa <see cref="CarVariant"/> và
/// <see cref="VehicleImage"/>: hai thứ này chỉ đổi qua root để Publish_Validation, khoảng giá, <c>search_text</c> và
/// giới hạn 30 ảnh luôn nhất quán trong MỘT transaction. Kế thừa <see cref="AuditedAggregateRoot{TKey}"/> nên có
/// <c>ConcurrencyStamp</c> (409 khi lệch phiên bản). Brand chỉ được tham chiếu bằng <see cref="BrandId"/>.
/// </summary>
public class CarModel : AuditedAggregateRoot<Guid>
{
    private readonly List<CarVariant> _variants = new();
    private readonly List<VehicleImage> _images = new();

    public Guid BrandId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Duy nhất trong Brand (<c>UNIQUE (brand_id, slug)</c>).</summary>
    public string Slug { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public CatalogStatus Status { get; private set; }

    /// <summary>
    /// <c>Normalize(brand + model + tên các variant Published)</c>, cột dùng cho tìm kiếm không dấu (ILIKE + pg_trgm).
    /// Cập nhật bằng <see cref="RefreshDerived"/> sau mỗi thay đổi ảnh hưởng.
    /// </summary>
    public string SearchText { get; private set; } = string.Empty;

    /// <summary>Giá thấp nhất/cao nhất của các variant Published có Reference_Price; null nếu không có (bỏ giá liên hệ).</summary>
    public long? MinPublishedPrice { get; private set; }

    public long? MaxPublishedPrice { get; private set; }

    /// <summary>Lần Published gần nhất. KHÔNG xóa khi Hidden, nên khác null nghĩa là "đã từng Published" (Req 8.13).</summary>
    public DateTime? PublishedAt { get; private set; }

    public bool IsFeatured { get; private set; }

    public int DisplayOrder { get; private set; }

    public bool IsDemo { get; private set; }

    public IReadOnlyCollection<CarVariant> Variants => _variants;

    public IReadOnlyCollection<VehicleImage> Images => _images;

    /// <summary>Đã từng Published (kể cả hiện đang Hidden): đổi Slug phải lưu lịch sử để chuyển hướng 301.</summary>
    public bool HasEverBeenPublished => PublishedAt.HasValue;

    private CarModel()
    {
        // EF Core
    }

    public CarModel(
        Guid id,
        Guid brandId,
        string brandName,
        string? name,
        string? slug,
        string? description = null,
        bool isFeatured = false,
        int displayOrder = 0,
        bool isDemo = false) : base(id)
    {
        var errors = Validate(name, slug, description, displayOrder);
        if (brandId == Guid.Empty)
        {
            errors.Add("brandId", "Brand là bắt buộc.");
        }

        errors.ThrowIfAny();

        BrandId = brandId;
        Name = name!.Trim();
        Slug = Slugs.Slug.Normalize(slug);
        Description = NormalizeDescription(description);
        Status = CatalogStatus.Draft;
        IsFeatured = isFeatured;
        DisplayOrder = displayOrder;
        IsDemo = isDemo;
        RefreshDerived(brandName);
    }

    // ---------------------------------------------------------------- nội dung

    /// <summary>
    /// Sửa nội dung model. Trả Slug CŨ nếu Slug đổi (để ghi lịch sử), ngược lại null.
    /// Model đang Published phải còn thỏa Publish_Validation sau khi sửa, nếu không ném 422 và (vì UoW không commit)
    /// giữ nguyên dữ liệu (Req 10.10).
    /// </summary>
    public string? Update(
        string brandName,
        BrandStatus brandStatus,
        string? name,
        string? slug,
        string? description,
        bool isFeatured,
        int displayOrder)
    {
        Validate(name, slug, description, displayOrder).ThrowIfAny();

        var previousSlug = Slugs.Slug.Normalize(slug) != Slug ? Slug : null;

        Name = name!.Trim();
        Slug = Slugs.Slug.Normalize(slug);
        Description = NormalizeDescription(description);
        IsFeatured = isFeatured;
        DisplayOrder = displayOrder;

        RefreshDerived(brandName);
        RevalidateIfPublished(brandStatus);
        return previousSlug;
    }

    public static FieldErrors Validate(string? name, string? slug, string? description, int displayOrder)
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

        if (description is { Length: > CatalogConsts.DescriptionMaxLength })
        {
            errors.Add("description", $"Mô tả dài tối đa {CatalogConsts.DescriptionMaxLength} ký tự.");
        }

        if (displayOrder is < CatalogConsts.DisplayOrderMin or > CatalogConsts.DisplayOrderMax)
        {
            errors.Add("displayOrder", $"Thứ tự hiển thị phải từ {CatalogConsts.DisplayOrderMin} đến {CatalogConsts.DisplayOrderMax}.");
        }

        return errors;
    }

    // ---------------------------------------------------------------- trạng thái

    /// <summary>
    /// Chuyển trạng thái theo <see cref="CatalogStatusMachine"/>. Sang Published thì chạy Publish_Validation và trả
    /// ĐỦ mọi quy tắc chưa thỏa (Req 10.3, 10.5). Bị từ chối thì trạng thái không đổi.
    /// </summary>
    public void ChangeStatus(CatalogStatus target, BrandStatus brandStatus, DateTime nowUtc)
    {
        var transition = CatalogStatusMachine.TryTransition(Status, target);
        if (!transition.Allowed)
        {
            throw CatalogExceptions.InvalidTransition(Status, target, transition.AllowedTargets);
        }

        if (target == CatalogStatus.Published)
        {
            var result = PublishValidator.ValidateCarModel(this, brandStatus);
            if (!result.IsValid)
            {
                throw CatalogExceptions.PublishValidationFailed(result);
            }

            PublishedAt = nowUtc;
        }

        Status = target;
    }

    /// <summary>Kiểm Publish_Validation mà không đổi gì (endpoint <c>publish-check</c>).</summary>
    public PublishValidationResult CheckPublishable(BrandStatus brandStatus) =>
        PublishValidator.ValidateCarModel(this, brandStatus);

    /// <summary>Model đang Published phải luôn thỏa Publish_Validation sau mọi thay đổi nội dung (Req 10.10).</summary>
    public void RevalidateIfPublished(BrandStatus brandStatus)
    {
        if (Status != CatalogStatus.Published)
        {
            return;
        }

        var result = PublishValidator.ValidateCarModel(this, brandStatus);
        if (!result.IsValid)
        {
            throw CatalogExceptions.PublishValidationFailed(result);
        }
    }

    // ---------------------------------------------------------------- variant

    public CarVariant GetVariant(Guid variantId) =>
        _variants.FirstOrDefault(v => v.Id == variantId)
        ?? throw new EntityNotFoundException(typeof(CarVariant), variantId);

    public CarVariant AddVariant(Guid variantId, CarVariantData data)
    {
        var errors = new FieldErrors();
        var normalized = CarVariant.Normalize(data, errors);
        errors.ThrowIfAny();
        EnsureVariantNameIsUnique(normalized.Name, exceptVariantId: null);

        var variant = new CarVariant(variantId, Id, normalized, IsDemo);
        _variants.Add(variant);
        return variant;
    }

    /// <summary>Sửa variant. Variant đang Published phải còn thỏa quy tắc Publish_Validation của CarVariant (Req 10.10).</summary>
    public CarVariant UpdateVariant(Guid variantId, CarVariantData data)
    {
        var variant = GetVariant(variantId);

        var errors = new FieldErrors();
        var normalized = CarVariant.Normalize(data, errors);
        errors.ThrowIfAny();
        EnsureVariantNameIsUnique(normalized.Name, variantId);

        variant.Apply(normalized);

        if (variant.Status == CatalogStatus.Published)
        {
            var result = PublishValidator.ValidateVariant(variant);
            if (!result.IsValid)
            {
                throw CatalogExceptions.PublishValidationFailed(result);
            }
        }

        return variant;
    }

    /// <summary>
    /// Chuyển trạng thái variant. Sang Published: Publish_Validation của variant. Rời Published (Hidden) khi variant
    /// là Published DUY NHẤT của model đang Published: 422 (Req 10.7).
    /// </summary>
    public void ChangeVariantStatus(Guid variantId, CatalogStatus target)
    {
        var variant = GetVariant(variantId);

        var transition = CatalogStatusMachine.TryTransition(variant.Status, target);
        if (!transition.Allowed)
        {
            throw CatalogExceptions.InvalidTransition(variant.Status, target, transition.AllowedTargets);
        }

        if (target == CatalogStatus.Published)
        {
            var result = PublishValidator.ValidateVariant(variant);
            if (!result.IsValid)
            {
                throw CatalogExceptions.PublishValidationFailed(result);
            }
        }
        else if (variant.Status == CatalogStatus.Published
                 && Status == CatalogStatus.Published
                 && !_variants.Any(v => v.Id != variantId && v.Status == CatalogStatus.Published))
        {
            throw CatalogExceptions.LastPublishedVariant();
        }

        variant.SetStatus(target);
    }

    private void EnsureVariantNameIsUnique(string name, Guid? exceptVariantId)
    {
        var duplicate = _variants.Any(v =>
            v.Id != exceptVariantId
            && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            throw CatalogExceptions.Duplicate("name");
        }
    }

    // ---------------------------------------------------------------- ảnh

    public VehicleImage GetImage(Guid imageId) =>
        _images.FirstOrDefault(i => i.Id == imageId)
        ?? throw new EntityNotFoundException(typeof(VehicleImage), imageId);

    /// <summary>
    /// Thêm ảnh đã lưu vào Object_Storage. Kiểm giới hạn 30 ẢNH ở đây, nhưng đảm bảo đúng khi upload đồng thời là việc của
    /// người gọi: phải khóa hàng CarModel (<c>SELECT … FOR UPDATE</c>) trước khi load aggregate.
    /// </summary>
    public VehicleImage AddImage(Guid imageId, string storageKey, string? alt, Guid? carVariantId = null)
    {
        if (Status == CatalogStatus.Archived)
        {
            throw CatalogExceptions.CarModelArchived();
        }

        if (_images.Count >= CatalogConsts.ImageMaxCount)
        {
            throw CatalogExceptions.ImageLimitReached();
        }

        var errors = new FieldErrors();
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > CatalogConsts.StorageKeyMaxLength)
        {
            errors.Add("storageKey", "Khóa lưu trữ không hợp lệ.");
        }

        var trimmedAlt = alt?.Trim() ?? string.Empty;
        if (trimmedAlt.Length > CatalogConsts.ImageAltMaxLength)
        {
            errors.Add("alt", $"Alt dài tối đa {CatalogConsts.ImageAltMaxLength} ký tự.");
        }

        if (carVariantId is { } variantId && _variants.All(v => v.Id != variantId))
        {
            errors.Add("carVariantId", "Phiên bản không thuộc mẫu xe này.");
        }

        errors.ThrowIfAny();

        var nextOrder = _images.Count == 0 ? 0 : _images.Max(i => i.DisplayOrder) + 1;
        var image = new VehicleImage(imageId, Id, carVariantId, storageKey, trimmedAlt, nextOrder);
        _images.Add(image);
        return image;
    }

    /// <summary>Bỏ cờ cover của ảnh hiện tại (bước 1 khi đổi cover, xem <see cref="SetCover"/>). Trả true nếu có đổi.</summary>
    public bool ClearCover()
    {
        var current = _images.Where(i => i.IsCover).ToList();
        foreach (var image in current)
        {
            image.SetCover(false);
        }

        return current.Count > 0;
    }

    /// <summary>
    /// Chọn cover: tối đa MỘT ảnh cover (Req 9.7). Partial unique index <c>(car_model_id) WHERE is_cover</c> kiểm ở DB,
    /// mà EF không bảo đảm thứ tự UPDATE, nên khi ĐỔI cover phải gọi <see cref="ClearCover"/>, lưu, rồi mới gọi hàm này.
    /// </summary>
    public void SetCover(Guid imageId)
    {
        var image = GetImage(imageId);
        if (_images.Any(i => i.IsCover && i.Id != imageId))
        {
            throw new InvalidOperationException("Phải gọi ClearCover và lưu trước khi đặt cover mới.");
        }

        image.SetCover(true);
    }

    // ---------------------------------------------------------------- dẫn xuất

    /// <summary>
    /// Tính lại các cột dẫn xuất (<c>search_text</c> và khoảng giá). Phải gọi sau MỌI thay đổi tên model, tên/trạng thái/giá
    /// của variant, hoặc tên Brand. <paramref name="brandName"/> do người gọi cung cấp vì model chỉ giữ BrandId.
    /// </summary>
    public void RefreshDerived(string brandName)
    {
        RecomputePriceRange();
        RecomputeSearchText(brandName);
    }

    /// <summary>Min/max Reference_Price của các variant Published; variant giá liên hệ bị bỏ qua; không có giá nào thì null.</summary>
    public void RecomputePriceRange()
    {
        var prices = _variants
            .Where(v => v.Status == CatalogStatus.Published && !v.ContactForPrice && v.ReferencePrice.HasValue)
            .Select(v => v.ReferencePrice!.Value)
            .ToList();

        MinPublishedPrice = prices.Count == 0 ? null : prices.Min();
        MaxPublishedPrice = prices.Count == 0 ? null : prices.Max();
    }

    public void RecomputeSearchText(string brandName)
    {
        var publishedNames = _variants
            .Where(v => v.Status == CatalogStatus.Published)
            .Select(v => v.Name)
            .OrderBy(n => n, StringComparer.Ordinal);

        SearchText = VietnameseText.Normalize(string.Join(' ', new[] { brandName, Name }.Concat(publishedNames)));
    }

    private static string? NormalizeDescription(string? description)
    {
        var trimmed = description?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
