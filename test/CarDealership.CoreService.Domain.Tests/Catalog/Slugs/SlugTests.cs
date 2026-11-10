using System;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Catalog.Slugs;

public class SlugTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("toyota")]
    [InlineData("toyota-vios-1-5g")]
    [InlineData("a-b")]
    [InlineData("a--b")]
    [InlineData("TOYOTA")]          // chuẩn hóa thành chữ thường rồi mới kiểm
    [InlineData("  toyota  ")]      // trim
    public void Valid_slugs_are_accepted(string input)
    {
        Slug.IsValid(input).ShouldBeTrue();
        Slug.Create(input).Value.ShouldBe(input.Trim().ToLowerInvariant());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-")]
    [InlineData("-toyota")]
    [InlineData("toyota-")]
    [InlineData("to yota")]
    [InlineData("to_yota")]
    [InlineData("toyota!")]
    [InlineData("đà-nẵng")]
    public void Invalid_slugs_are_rejected(string? input)
    {
        Slug.IsValid(input).ShouldBeFalse();
        Slug.GetError(input).ShouldNotBeNull();
        Should.Throw<ArgumentException>(() => Slug.Create(input));
        Slug.TryCreate(input, out var slug).ShouldBeFalse();
        slug.ShouldBeNull();
    }

    [Fact]
    public void Length_boundary_is_100()
    {
        Slug.IsValid(new string('a', 100)).ShouldBeTrue();
        Slug.IsValid(new string('a', 101)).ShouldBeFalse();
        Slug.IsValid("a" + new string('-', 98) + "a").ShouldBeTrue();
    }

    [Fact]
    public void Slugs_with_same_value_are_equal()
    {
        Slug.Create("toyota").Equals(Slug.Create("  Toyota ")).ShouldBeTrue();
        Slug.Create("toyota").Equals(Slug.Create("honda")).ShouldBeFalse();
    }
}

public class SlugGeneratorTests
{
    [Theory]
    [InlineData("Toyota Vios 1.5G", "toyota-vios-1-5g")]
    [InlineData("Đà Nẵng", "da-nang")]
    [InlineData("  VinFast   VF 8  ", "vinfast-vf-8")]
    [InlineData("Xe điện: Hạng A/B", "xe-dien-hang-a-b")]
    [InlineData("Mazda--CX-5", "mazda-cx-5")]
    [InlineData("a", "a")]
    public void Name_becomes_a_slug(string name, string expected)
    {
        SlugGenerator.FromName(name).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("---")]
    [InlineData("@#$%^&*()")]
    public void Names_without_letters_or_digits_give_an_empty_suggestion(string? name)
    {
        SlugGenerator.FromName(name).ShouldBe(string.Empty);
    }

    [Fact]
    public void Result_is_cut_to_100_characters_without_a_trailing_hyphen()
    {
        var longName = string.Join(" ", new string('a', 49), new string('b', 49), "cccccc");

        var slug = SlugGenerator.FromName(longName);

        slug.Length.ShouldBeLessThanOrEqualTo(100);
        slug.ShouldNotEndWith("-");
        Slug.IsValid(slug).ShouldBeTrue();
    }

    [Fact]
    public void Cutting_exactly_before_a_hyphen_does_not_leave_it_behind()
    {
        // 99 chữ 'a' rồi khoảng trắng rồi 'b': cắt ở 100 ký tự sẽ rơi đúng vào dấu '-'.
        var slug = SlugGenerator.FromName(new string('a', 99) + " b");

        slug.ShouldBe(new string('a', 99));
    }

    [Theory]
    [InlineData("Toyota Vios 1.5G")]
    [InlineData("Đà Nẵng - Quận 1")]
    [InlineData("Phiên bản Đặc Biệt 2.0 Turbo")]
    public void Generated_slugs_are_always_valid(string name)
    {
        var slug = SlugGenerator.FromName(name);

        slug.ShouldNotBeEmpty();
        Slug.IsValid(slug).ShouldBeTrue();
    }
}
