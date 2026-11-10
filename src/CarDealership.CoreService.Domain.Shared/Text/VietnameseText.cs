using System.Globalization;
using System.Text;

namespace CarDealership.CoreService.Text;

/// <summary>
/// Xử lý chuỗi tiếng Việt bằng hàm C# thuần (không phụ thuộc collation hay extension của DB):
/// bỏ dấu, chuẩn hóa để tìm kiếm không phân biệt dấu và hoa thường.
/// </summary>
public static class VietnameseText
{
    /// <summary>
    /// Bỏ dấu: phân rã FormD rồi loại ký tự <see cref="UnicodeCategory.NonSpacingMark"/>.
    /// <c>đ/Đ</c> KHÔNG phân rã bằng FormD nên được thay riêng thành <c>d/D</c>.
    /// Giữ nguyên hoa/thường của các ký tự còn lại.
    /// </summary>
    public static string RemoveDiacritics(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            if (c == '\u0111') // đ
            {
                builder.Append('d');
                continue;
            }

            if (c == '\u0110') // Đ
            {
                builder.Append('D');
                continue;
            }

            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Chuẩn hóa cho tìm kiếm: bỏ dấu, chữ thường, gộp mọi chuỗi khoảng trắng thành một dấu cách, trim.
    /// Idempotent: <c>Normalize(Normalize(x)) == Normalize(x)</c>.
    /// </summary>
    public static string Normalize(string? value)
    {
        var text = RemoveDiacritics(value).ToLowerInvariant();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
