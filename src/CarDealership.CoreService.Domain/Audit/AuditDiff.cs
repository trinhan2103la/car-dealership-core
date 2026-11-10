using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CarDealership.CoreService.Audit;

/// <summary>
/// Một dòng thay đổi của Audit_Log. <see cref="Sensitive"/> = true thì <see cref="Before"/> và <see cref="After"/>
/// luôn null: trường nhạy cảm chỉ được ghi bằng TÊN (Req 17.5), không bao giờ ghi giá trị.
/// </summary>
public sealed record AuditChange(string Field, JsonNode? Before, JsonNode? After, bool Sensitive = false);

/// <summary>
/// So sánh hai ảnh chụp (snapshot) của một thực thể và sinh danh sách thay đổi <c>{field, before, after}</c>.
/// Hàm thuần: không đọc DB, đồng hồ hay ngữ cảnh người dùng.
/// - <c>before = null</c> là tạo mới: mọi trường có giá trị trong <c>after</c> là một thay đổi (trước rỗng).
/// - <c>after = null</c> là xóa: mọi trường có giá trị trong <c>before</c> là một thay đổi (sau rỗng).
/// - Không có trường nào khác nhau thì trả danh sách rỗng (Req 17.2: không ghi Audit_Log).
/// </summary>
public static class AuditDiff
{
    /// <summary>Tên trường nhạy cảm (không phân biệt hoa thường). Chỉ ghi tên, không ghi giá trị.</summary>
    public static readonly IReadOnlySet<string> SensitiveFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "passwordHash",
        "securityStamp",
        "token",
        "secret",
    };

    private static readonly JsonSerializerOptions ValueOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<AuditChange> Compute(
        IReadOnlyDictionary<string, object?>? before,
        IReadOnlyDictionary<string, object?>? after)
    {
        var fields = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in (after?.Keys ?? Enumerable.Empty<string>()).Concat(before?.Keys ?? Enumerable.Empty<string>()))
        {
            if (seen.Add(key))
            {
                fields.Add(key);
            }
        }

        var changes = new List<AuditChange>();
        foreach (var field in fields)
        {
            var beforeNode = ToNode(before, field);
            var afterNode = ToNode(after, field);

            if (JsonNode.DeepEquals(beforeNode, afterNode))
            {
                continue;
            }

            changes.Add(SensitiveFields.Contains(field)
                ? new AuditChange(field, null, null, Sensitive: true)
                : new AuditChange(field, beforeNode, afterNode));
        }

        return changes;
    }

    /// <summary>Mảng JSON cho cột <c>changes</c>. Trường nhạy cảm chỉ có khóa <c>field</c>.</summary>
    public static string ToJson(IReadOnlyList<AuditChange> changes)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var change in changes)
            {
                writer.WriteStartObject();
                writer.WriteString("field", change.Field);

                if (!change.Sensitive)
                {
                    writer.WritePropertyName("before");
                    WriteNode(writer, change.Before);
                    writer.WritePropertyName("after");
                    WriteNode(writer, change.After);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteNode(Utf8JsonWriter writer, JsonNode? node)
    {
        if (node is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            node.WriteTo(writer);
        }
    }

    private static JsonNode? ToNode(IReadOnlyDictionary<string, object?>? snapshot, string field)
    {
        if (snapshot is null || !snapshot.TryGetValue(field, out var value) || value is null)
        {
            return null;
        }

        return JsonSerializer.SerializeToNode(value, value.GetType(), ValueOptions);
    }
}
