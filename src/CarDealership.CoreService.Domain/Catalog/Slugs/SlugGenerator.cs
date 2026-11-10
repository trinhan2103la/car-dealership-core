using System.Text;
using CarDealership.CoreService.Text;

namespace CarDealership.CoreService.Catalog.Slugs;

/// <summary>Gợi ý Slug từ tên: bỏ dấu (gồm <c>đ → d</c>), chữ thường, mọi chuỗi ký tự ngoài [a-z0-9] thành một dấu <c>-</c>.</summary>
public static class SlugGenerator
{
    /// <summary>
    /// Trả chuỗi rỗng nếu tên không có ký tự chữ hoặc số nào (ví dụ <c>"!!!"</c>): người gọi phải coi đó là
    /// "không gợi ý được", không được dùng làm Slug.
    /// </summary>
    public static string FromName(string? name)
    {
        var plain = VietnameseText.RemoveDiacritics(name).ToLowerInvariant();
        var builder = new StringBuilder(plain.Length);
        var pendingHyphen = false;

        foreach (var c in plain)
        {
            var isAlnum = c is >= 'a' and <= 'z' or >= '0' and <= '9';
            if (isAlnum)
            {
                if (pendingHyphen && builder.Length > 0)
                {
                    builder.Append('-');
                }

                pendingHyphen = false;
                builder.Append(c);
            }
            else
            {
                pendingHyphen = true;
            }

            if (builder.Length >= Slug.MaxLength)
            {
                break;
            }
        }

        // Cắt đúng 100 ký tự, rồi bỏ '-' cuối nếu bị cắt giữa chừng.
        var result = builder.Length > Slug.MaxLength ? builder.ToString(0, Slug.MaxLength) : builder.ToString();
        return result.TrimEnd('-');
    }
}
