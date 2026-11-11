using System;
using System.Linq;
using CarDealership.CoreService.Catalog.Slugs;
using FsCheck;
using FsCheck.Fluent;
using Xunit;

namespace CarDealership.CoreService.Catalog.Properties;

/// <summary>
/// Property 13, phần validator (task 3.23, Req 7.1): Slug được chấp nhận khi và chỉ khi (sau trim + chữ thường) dài 1–100,
/// chỉ gồm [a-z0-9-], không bắt đầu/kết thúc bằng '-'; Slug gợi ý từ tên luôn hợp lệ (hoặc rỗng = "không gợi ý được").
/// Phần chuyển hướng một bước chạy trên DB thật: <c>SlugRedirectProperties</c> (EF Core tests).
/// </summary>
public class SlugProperties
{
    private static Config Runs(int n) => Config.QuickThrowOnFailure.WithMaxTest(n);

    /// <summary>Đặc tả viết lại bằng vòng lặp ký tự, độc lập với regex của <see cref="Slug"/>.</summary>
    private static bool ReferenceIsValid(string? input)
    {
        var s = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (s.Length is < 1 or > 100 || s[0] == '-' || s[^1] == '-')
        {
            return false;
        }

        return s.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    }

    /// <summary>Chuỗi gần-Slug (để trúng nhánh hợp lệ thường xuyên) trộn với chuỗi bất kỳ, gồm cả độ dài quanh 100.</summary>
    private static readonly Gen<string> SlugLike =
        from length in Gen.Frequency((6, Gen.Choose(0, 12)), (2, Gen.Choose(95, 105)))
        from chars in Gen.Elements("abcxyz0129-- _AZĐđéK\t\u212A".ToCharArray()).ArrayOf(length)
        select new string(chars);

    private static readonly Arbitrary<string> Inputs =
        Gen.Frequency((3, SlugLike), (1, ArbMap.Default.GeneratorFor<string>())).ToArbitrary();

    [Fact]
    public void Validator_accepts_exactly_the_specified_language()
    {
        Check.One(Runs(2000), Prop.ForAll(Inputs, s => Slug.IsValid(s) == ReferenceIsValid(s)));
    }

    [Fact]
    public void Accepted_slugs_normalize_to_themselves()
    {
        Check.One(Runs(1000), Prop.ForAll(Inputs, s =>
            !Slug.IsValid(s) || (Slug.Create(s).Value == Slug.Normalize(s) && Slug.IsValid(Slug.Normalize(s)))));
    }

    private static readonly Arbitrary<string> Names =
        Gen.Frequency(
                (3, Gen.Elements("Vios", "Corolla Cross", "CR-V", "Đông Phong", "Xe điện VF 8", "Ô tô  Hà Nội!!", "!!!", "   ", "Tucson 1.6 Turbo HTRAC")
                    .SelectMany(prefix => Gen.Choose(0, 120).Select(n => prefix + new string('x', n)))),
                (2, ArbMap.Default.GeneratorFor<string>()))
            .ToArbitrary();

    [Fact]
    public void Generated_slug_is_always_valid_or_empty()
    {
        Check.One(Runs(2000), Prop.ForAll(Names, name =>
        {
            var slug = SlugGenerator.FromName(name);
            return slug.Length == 0 || (Slug.IsValid(slug) && slug == Slug.Normalize(slug));
        }));
    }

    [Fact]
    public void Names_with_an_ascii_letter_or_digit_always_get_a_suggestion()
    {
        Check.One(Runs(2000), Prop.ForAll(Names, name =>
            !name.Any(char.IsAsciiLetterOrDigit) || SlugGenerator.FromName(name).Length > 0));
    }
}
