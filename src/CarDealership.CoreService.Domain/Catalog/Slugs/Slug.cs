using System.Collections.Generic;
using System.Text.RegularExpressions;
using Volo.Abp.Domain.Values;

namespace CarDealership.CoreService.Catalog.Slugs;

/// <summary>
/// Value object Slug (Req 7.1, 8.2): 1–100 ký tự, chỉ <c>[a-z0-9-]</c>, không bắt đầu hoặc kết thúc bằng <c>-</c>.
/// Đầu vào được trim và đổi sang chữ thường TRƯỚC khi kiểm, nên <c>" Toyota "</c> thành <c>toyota</c> và việc trùng Slug
/// không phân biệt hoa thường. Entity lưu <c>string</c>; lớp này là nơi duy nhất định nghĩa thế nào là Slug hợp lệ.
/// </summary>
public sealed class Slug : ValueObject
{
    public const int MaxLength = CatalogConsts.SlugMaxLength;

    private static readonly Regex Pattern = new(
        "^[a-z0-9]([a-z0-9-]*[a-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string Value { get; }

    private Slug(string value)
    {
        Value = value;
    }

    /// <summary>Trim + chữ thường. Không bỏ dấu và không đổi ký tự lạ (đó là việc của <see cref="SlugGenerator"/>).</summary>
    public static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Thông báo lỗi nếu <paramref name="value"/> (sau khi chuẩn hóa) không hợp lệ, ngược lại null.</summary>
    public static string? GetError(string? value)
    {
        var normalized = Normalize(value);

        if (normalized.Length == 0)
        {
            return "Slug không được rỗng.";
        }

        if (normalized.Length > MaxLength)
        {
            return $"Slug dài tối đa {MaxLength} ký tự.";
        }

        return Pattern.IsMatch(normalized)
            ? null
            : "Slug chỉ gồm a-z, 0-9 và dấu '-', không bắt đầu hoặc kết thúc bằng '-'.";
    }

    public static bool IsValid(string? value) => GetError(value) is null;

    public static bool TryCreate(string? value, out Slug? slug)
    {
        if (IsValid(value))
        {
            slug = new Slug(Normalize(value));
            return true;
        }

        slug = null;
        return false;
    }

    /// <summary>Tạo Slug hoặc ném <see cref="System.ArgumentException"/> (dùng ở nơi đã validate trước).</summary>
    public static Slug Create(string? value)
    {
        var error = GetError(value);
        if (error is not null)
        {
            throw new System.ArgumentException(error, nameof(value));
        }

        return new Slug(Normalize(value));
    }

    protected override IEnumerable<object> GetAtomicValues()
    {
        yield return Value;
    }

    // ABP ValueObject không override Equals (chỉ có ValueEquals), nên ghi đè để so sánh theo giá trị đúng như người dùng kỳ vọng.
    public override bool Equals(object? obj) => obj is Slug other && string.Equals(Value, other.Value, System.StringComparison.Ordinal);

    public override int GetHashCode() => Value.GetHashCode(System.StringComparison.Ordinal);

    public override string ToString() => Value;
}
