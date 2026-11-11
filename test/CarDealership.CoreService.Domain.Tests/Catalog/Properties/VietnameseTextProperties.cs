using System.Linq;
using CarDealership.CoreService.Text;
using FsCheck;
using FsCheck.Fluent;
using Xunit;

namespace CarDealership.CoreService.Catalog.Properties;

/// <summary>
/// Property 10 (task 3.25, Req 2.8): với mọi chuỗi tiếng Việt s và mọi chuỗi con t của s, Normalize(s) chứa Normalize(t);
/// bỏ dấu hoặc đổi hoa/thường của t không làm đổi Normalize(t); Normalize idempotent.
/// </summary>
public class VietnameseTextProperties
{
    private static Config Runs(int n) => Config.QuickThrowOnFailure.WithMaxTest(n);

    /// <summary>Đủ nguyên âm có dấu (hoa + thường), đ/Đ, chữ ASCII, số và các loại khoảng trắng.</summary>
    private const string Alphabet =
        "aăâeêioôơuưyAĂÂEÊIOÔƠUƯY" +
        "áàảãạấầẩẫậắằẳẵặéèẻẽẹếềểễệíìỉĩịóòỏõọốồổỗộớờởỡợúùủũụứừửữựýỳỷỹỵ" +
        "ÁÀẢÃẠẤẦẨẪẬẮẰẲẴẶÉÈẺẼẸẾỀỂỄỆÍÌỈĨỊÓÒỎÕỌỐỒỔỖỘỚỜỞỠỢÚÙỦŨỤỨỪỬỮỰÝỲỶỸỴ" +
        "đĐbcdghklmnpqrstvxBCDGHKLMNPQRSTVX0123456789-.  \t\u00A0";

    private static readonly Gen<string> Vietnamese =
        from length in Gen.Choose(0, 40)
        from chars in Gen.Elements(Alphabet.ToCharArray()).ArrayOf(length)
        select new string(chars);

    private static readonly Arbitrary<(string S, string T)> WithSubstring =
        (from s in Vietnamese
         from start in Gen.Choose(0, s.Length)
         from length in Gen.Choose(0, s.Length - start)
         select (s, s.Substring(start, length))).ToArbitrary();

    [Fact]
    public void Normalized_string_contains_every_normalized_substring()
    {
        Check.One(Runs(2000), Prop.ForAll(WithSubstring, p =>
            VietnameseText.Normalize(p.S).Contains(VietnameseText.Normalize(p.T), System.StringComparison.Ordinal)));
    }

    [Fact]
    public void Removing_accents_or_changing_case_does_not_change_the_normalized_form()
    {
        Check.One(Runs(2000), Prop.ForAll(Vietnamese.ToArbitrary(), t =>
        {
            var n = VietnameseText.Normalize(t);
            return VietnameseText.Normalize(VietnameseText.RemoveDiacritics(t)) == n
                   && VietnameseText.Normalize(t.ToUpperInvariant()) == n
                   && VietnameseText.Normalize(t.ToLowerInvariant()) == n;
        }));
    }

    [Fact]
    public void Normalized_form_is_accent_free_lowercase_and_has_no_double_or_edge_spaces()
    {
        Check.One(Runs(2000), Prop.ForAll(Vietnamese.ToArbitrary(), t =>
        {
            var n = VietnameseText.Normalize(t);
            return n == n.Trim() && !n.Contains("  ") && n == n.ToLowerInvariant()
                   && n.All(c => c < 0x0300 || c > 0x036F) && !n.Contains('đ');
        }));
    }

    [Fact]
    public void Normalize_is_idempotent_for_vietnamese_text()
    {
        Check.One(Runs(2000), Prop.ForAll(Vietnamese.ToArbitrary(), t =>
            VietnameseText.Normalize(VietnameseText.Normalize(t)) == VietnameseText.Normalize(t)));
    }

    [Fact]
    public void Normalize_is_idempotent_for_any_string()
    {
        Check.One(Runs(2000), Prop.ForAll(ArbMap.Default.ArbFor<string>(), t =>
            VietnameseText.Normalize(VietnameseText.Normalize(t)) == VietnameseText.Normalize(t)));
    }
}
