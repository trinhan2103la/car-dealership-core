using System.Collections.Generic;
using System.Linq;
using CarDealership.CoreService.Catalog.Publishing;
using Volo.Abp;

namespace CarDealership.CoreService.Catalog;

/// <summary>Một CarModel Published chặn việc chuyển Brand sang Inactive (Req 7.5).</summary>
public sealed record BlockingCarModel(string Name, string Slug);

/// <summary>
/// Tạo <see cref="BusinessException"/> của Catalog với mã ổn định và dữ liệu kèm theo (trả trong <c>error.data</c>).
/// Tên khóa trong Data trùng tên trường ProblemDetails của BFF (lệch A): <c>field</c>, <c>currentState</c>,
/// <c>allowedTargets</c>, <c>rules</c>, <c>items</c>. Ánh xạ mã → HTTP nằm ở <c>CoreServiceHttpApiModule</c>.
/// </summary>
public static class CatalogExceptions
{
    public static BusinessException Duplicate(string field) =>
        new BusinessException(CatalogErrorCodes.Duplicate, $"Giá trị của '{field}' đã tồn tại.")
            .WithData("field", field);

    public static BusinessException InvalidTransition(CatalogStatus current, CatalogStatus requested, IReadOnlyList<CatalogStatus> allowedTargets) =>
        new BusinessException(
                CatalogErrorCodes.InvalidTransition,
                $"Không thể chuyển từ {current} sang {requested}.")
            .WithData("currentState", current.ToString())
            .WithData("allowedTargets", allowedTargets.Select(s => s.ToString()).ToArray());

    public static BusinessException PublishValidationFailed(PublishValidationResult result) =>
        new BusinessException(CatalogErrorCodes.PublishValidationFailed, "Chưa đủ điều kiện xuất bản.")
            .WithData("rules", result.Violations
                .Select(code => new Dictionary<string, object>
                {
                    ["code"] = code,
                    ["passed"] = false,
                    ["message"] = PublishRuleMessages.For(code),
                })
                .ToArray())
            .WithData("invalidImageIds", result.InvalidImageIds.Select(i => i.ToString()).ToArray());

    public static BusinessException LastPublishedVariant() =>
        new BusinessException(
            CatalogErrorCodes.LastPublishedVariant,
            "Đây là phiên bản Published duy nhất của một mẫu xe đang Published. Hãy ẩn mẫu xe trước.");

    public static BusinessException BrandHasPublishedCarModels(IReadOnlyList<BlockingCarModel> carModels) =>
        new BusinessException(
                CatalogErrorCodes.BrandHasPublishedCarModels,
                "Brand còn mẫu xe Published. Hãy ẩn các mẫu xe này trước.")
            .WithData("items", carModels.Select(c => new Dictionary<string, string> { ["name"] = c.Name, ["slug"] = c.Slug }).ToArray());

    public static BusinessException CarModelArchived() =>
        new BusinessException(CatalogErrorCodes.CarModelArchived, "Mẫu xe đã lưu trữ, không nhận thêm ảnh.");

    public static BusinessException ImageLimitReached() =>
        new BusinessException(
                CatalogErrorCodes.ImageLimitReached,
                $"Mỗi mẫu xe tối đa {CatalogConsts.ImageMaxCount} ảnh.")
            .WithData("max", CatalogConsts.ImageMaxCount);
}
