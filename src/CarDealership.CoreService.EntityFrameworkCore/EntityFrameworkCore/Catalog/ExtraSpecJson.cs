using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CarDealership.CoreService.Catalog.CarModels;

namespace CarDealership.CoreService.EntityFrameworkCore.Catalog;

/// <summary>
/// Chuyển <c>IReadOnlyList&lt;ExtraSpec&gt;</c> ↔ mảng JSON của cột <c>extra_specs jsonb</c> và cung cấp các hàm so sánh cho
/// <c>ValueComparer</c> (EF phải so sánh theo NỘI DUNG, nếu không sẽ không phát hiện danh sách đã đổi).
/// Khóa JSON camelCase: <c>[{"group":"Kích thước","name":"Dài","value":"4425","unit":"mm"}]</c>.
/// </summary>
internal static class ExtraSpecJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Write(IReadOnlyList<ExtraSpec> value) => JsonSerializer.Serialize(value, Options);

    public static IReadOnlyList<ExtraSpec> Read(string json) =>
        JsonSerializer.Deserialize<List<ExtraSpec>>(json, Options) ?? new List<ExtraSpec>();

    public static bool SequenceEqual(IReadOnlyList<ExtraSpec>? a, IReadOnlyList<ExtraSpec>? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        return a is not null && b is not null && a.SequenceEqual(b);
    }

    public static int Hash(IReadOnlyList<ExtraSpec> value)
    {
        var hash = new HashCode();
        foreach (var item in value)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    public static IReadOnlyList<ExtraSpec> Copy(IReadOnlyList<ExtraSpec> value) => value.ToList();
}
