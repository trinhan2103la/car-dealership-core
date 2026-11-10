using System;
using System.Collections.Generic;

namespace CarDealership.CoreService.Catalog.CarModels;

/// <summary>Một dòng Extra_Spec như client gửi (chưa chuẩn hóa).</summary>
public sealed record ExtraSpecInput(string? Group, string? Name, string? Value, string? Unit = null);

/// <summary>
/// Value object Extra_Spec (Req 8.8): nhóm 1–100, tên 1–100 duy nhất trong nhóm (không phân biệt hoa thường),
/// giá trị 1–255, đơn vị tùy chọn ≤ 20, tối đa 200 mục cho mỗi CarVariant. Lưu trong cột <c>extra_specs</c> kiểu jsonb.
/// Bất biến (record) nên so sánh theo giá trị; thay cả danh sách khi sửa.
/// </summary>
public sealed record ExtraSpec(string Group, string Name, string Value, string? Unit = null)
{
    /// <summary>
    /// Kiểm toàn bộ danh sách, gom MỌI lỗi theo chỉ mục (<c>extraSpecs[3].name</c>) vào <paramref name="errors"/>
    /// và trả danh sách đã chuẩn hóa (trim, đơn vị rỗng thành null).
    /// </summary>
    public static IReadOnlyList<ExtraSpec> Normalize(
        IReadOnlyList<ExtraSpecInput>? inputs,
        FieldErrors errors,
        string member = "extraSpecs")
    {
        var result = new List<ExtraSpec>();
        if (inputs is null || inputs.Count == 0)
        {
            return result;
        }

        if (inputs.Count > CatalogConsts.ExtraSpecMaxCount)
        {
            errors.Add(member, $"Tối đa {CatalogConsts.ExtraSpecMaxCount} Extra_Spec cho mỗi phiên bản.");
        }

        var seen = new HashSet<(string Group, string Name)>();

        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var prefix = $"{member}[{i}]";

            var group = input.Group?.Trim() ?? string.Empty;
            var name = input.Name?.Trim() ?? string.Empty;
            var value = input.Value?.Trim() ?? string.Empty;
            var unit = string.IsNullOrWhiteSpace(input.Unit) ? null : input.Unit.Trim();

            RequireLength(errors, $"{prefix}.group", group, 1, CatalogConsts.ExtraSpecGroupMaxLength, "Nhóm");
            RequireLength(errors, $"{prefix}.name", name, 1, CatalogConsts.ExtraSpecNameMaxLength, "Tên");
            RequireLength(errors, $"{prefix}.value", value, 1, CatalogConsts.ExtraSpecValueMaxLength, "Giá trị");

            if (unit is { Length: > CatalogConsts.ExtraSpecUnitMaxLength })
            {
                errors.Add($"{prefix}.unit", $"Đơn vị dài tối đa {CatalogConsts.ExtraSpecUnitMaxLength} ký tự.");
            }

            // Tên duy nhất trong cùng nhóm, không phân biệt hoa thường (chỉ kiểm khi cả hai không rỗng).
            if (group.Length > 0 && name.Length > 0
                && !seen.Add((group.ToLowerInvariant(), name.ToLowerInvariant())))
            {
                errors.Add($"{prefix}.name", "Tên đã tồn tại trong cùng nhóm.");
            }

            result.Add(new ExtraSpec(group, name, value, unit));
        }

        return result;
    }

    private static void RequireLength(FieldErrors errors, string member, string value, int min, int max, string label)
    {
        if (value.Length < min)
        {
            errors.Add(member, $"{label} không được rỗng.");
        }
        else if (value.Length > max)
        {
            errors.Add(member, $"{label} dài tối đa {max} ký tự.");
        }
    }
}
