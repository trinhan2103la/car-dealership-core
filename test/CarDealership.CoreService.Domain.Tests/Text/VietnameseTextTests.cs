using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Text;

public class VietnameseTextTests
{
    [Theory]
    [InlineData("Đà Nẵng", "Da Nang")]
    [InlineData("đường", "duong")]
    [InlineData("ĐIỆN", "DIEN")]
    [InlineData("Xe điện", "Xe dien")]
    [InlineData("Phiên bản cao cấp", "Phien ban cao cap")]
    [InlineData("Hyundai i10", "Hyundai i10")]
    [InlineData("", "")]
    public void RemoveDiacritics_removes_marks_and_maps_d_stroke(string input, string expected)
    {
        VietnameseText.RemoveDiacritics(input).ShouldBe(expected);
    }

    [Fact]
    public void RemoveDiacritics_handles_null_and_decomposed_input()
    {
        VietnameseText.RemoveDiacritics(null).ShouldBe(string.Empty);
        // "ệ" ở dạng phân rã sẵn (e + dấu nặng + dấu mũ)
        VietnameseText.RemoveDiacritics("e\u0323\u0302").ShouldBe("e");
    }

    [Theory]
    [InlineData("XE ĐIỆN", "xe dien")]
    [InlineData("  Toyota   Vios\t1.5G  ", "toyota vios 1.5g")]
    [InlineData("Đà\nNẵng", "da nang")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Normalize_lowercases_removes_diacritics_and_collapses_whitespace(string? input, string expected)
    {
        VietnameseText.Normalize(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Phiên bản Đặc Biệt  2.0")]
    [InlineData("TOYOTA   VIOS")]
    [InlineData("")]
    public void Normalize_is_idempotent(string input)
    {
        var once = VietnameseText.Normalize(input);
        VietnameseText.Normalize(once).ShouldBe(once);
    }

    [Fact]
    public void Normalized_haystack_contains_normalized_needle_regardless_of_accents_and_case()
    {
        var haystack = VietnameseText.Normalize("Toyota Vios Phiên bản Điện");

        haystack.ShouldContain(VietnameseText.Normalize("DIEN"));
        haystack.ShouldContain(VietnameseText.Normalize("phiên"));
        haystack.ShouldContain(VietnameseText.Normalize("Phien Ban"));
    }
}
