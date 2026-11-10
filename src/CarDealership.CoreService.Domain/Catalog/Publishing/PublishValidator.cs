using System;
using System.Collections.Generic;
using System.Linq;
using CarDealership.CoreService.Catalog.CarModels;

namespace CarDealership.CoreService.Catalog.Publishing;

/// <summary>Kết quả Publish_Validation: danh sách ĐẦY ĐỦ mã quy tắc chưa thỏa (không dừng ở lỗi đầu tiên).</summary>
public sealed record PublishValidationResult(
    IReadOnlyList<string> Violations,
    IReadOnlyList<Guid> InvalidImageIds)
{
    public bool IsValid => Violations.Count == 0;

    public static PublishValidationResult Valid { get; } = new(Array.Empty<string>(), Array.Empty<Guid>());
}

/// <summary>
/// Publish_Validation (Req 10.3, 10.4). Mỗi quy tắc là một <see cref="Rule{T}"/> ĐỘC LẬP, <c>Validate</c> chạy hết cả danh sách
/// rồi gom kết quả, nên 3 quy tắc vi phạm trả đúng 3 mã. Hàm thuần: Brand status do người gọi truyền vào.
/// </summary>
public static class PublishValidator
{
    private sealed record Rule<T>(string Code, Func<T, bool> IsSatisfied);

    private static readonly IReadOnlyList<Rule<CarVariant>> VariantRules = new List<Rule<CarVariant>>
    {
        new(PublishRuleCodes.KeySpecComplete, v => v.KeySpec.IsComplete),
        new(PublishRuleCodes.PriceValid, HasValidPrice),
    };

    /// <summary>Mã quy tắc của CarVariant, theo thứ tự đánh giá (cho test và checklist).</summary>
    public static IReadOnlyList<string> VariantRuleCodes { get; } = VariantRules.Select(r => r.Code).ToList();

    /// <summary>Mã quy tắc của CarModel, theo thứ tự đánh giá.</summary>
    public static IReadOnlyList<string> CarModelRuleCodes { get; } = new[]
    {
        PublishRuleCodes.BrandActive,
        PublishRuleCodes.DescriptionLength,
        PublishRuleCodes.SingleCoverImage,
        PublishRuleCodes.ImageAlt,
        PublishRuleCodes.HasPublishedVariant,
    };

    public static PublishValidationResult ValidateVariant(CarVariant variant)
    {
        var violations = VariantRules.Where(r => !r.IsSatisfied(variant)).Select(r => r.Code).ToList();
        return violations.Count == 0
            ? PublishValidationResult.Valid
            : new PublishValidationResult(violations, Array.Empty<Guid>());
    }

    public static PublishValidationResult ValidateCarModel(CarModel model, BrandStatus brandStatus)
    {
        var violations = new List<string>();

        if (brandStatus != BrandStatus.Active)
        {
            violations.Add(PublishRuleCodes.BrandActive);
        }

        var descriptionLength = model.Description?.Trim().Length ?? 0;
        if (descriptionLength is < CatalogConsts.DescriptionPublishMinLength or > CatalogConsts.DescriptionMaxLength)
        {
            violations.Add(PublishRuleCodes.DescriptionLength);
        }

        if (model.Images.Count(i => i.IsCover) != 1)
        {
            violations.Add(PublishRuleCodes.SingleCoverImage);
        }

        var invalidImages = model.Images.Where(i => !i.HasPublishableAlt()).Select(i => i.Id).ToList();
        if (invalidImages.Count > 0)
        {
            violations.Add(PublishRuleCodes.ImageAlt);
        }

        if (!model.Variants.Any(v => v.Status == CatalogStatus.Published))
        {
            violations.Add(PublishRuleCodes.HasPublishedVariant);
        }

        return violations.Count == 0
            ? PublishValidationResult.Valid
            : new PublishValidationResult(violations, invalidImages);
    }

    private static bool HasValidPrice(CarVariant variant)
    {
        // Đúng một trong hai: giá hợp lệ trong (0, 100 tỷ] HOẶC cờ liên hệ.
        if (variant.ContactForPrice)
        {
            return variant.ReferencePrice is null;
        }

        return variant.ReferencePrice is >= CatalogConsts.PriceMin and <= CatalogConsts.PriceMax;
    }
}
