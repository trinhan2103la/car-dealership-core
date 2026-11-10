using System;
using System.Linq;
using Shouldly;
using Volo.Abp.Validation;
using Xunit;

namespace CarDealership.CoreService.Catalog.Brands;

public class BrandTests
{
    [Fact]
    public void Valid_brand_is_trimmed_normalized_and_active_by_default()
    {
        var brand = new Brand(Guid.NewGuid(), "  Toyota  ", " TOYOTA ");

        brand.Name.ShouldBe("Toyota");
        brand.Slug.ShouldBe("toyota");
        brand.Status.ShouldBe(BrandStatus.Active);
        brand.LogoKey.ShouldBeNull();
    }

    [Theory]
    [InlineData("", "toyota", "name")]
    [InlineData("   ", "toyota", "name")]
    [InlineData("Toyota", "-toyota", "slug")]
    [InlineData("Toyota", "toyota-", "slug")]
    [InlineData("Toyota", "", "slug")]
    public void Invalid_input_is_rejected_with_the_field_name(string name, string slug, string field)
    {
        var ex = Should.Throw<AbpValidationException>(() => new Brand(Guid.NewGuid(), name, slug));

        ex.ValidationErrors.SelectMany(e => e.MemberNames).ShouldContain(field);
    }

    [Fact]
    public void Name_and_slug_length_boundaries_are_100()
    {
        new Brand(Guid.NewGuid(), new string('a', 100), new string('a', 100)).ShouldNotBeNull();

        Brand.Validate(new string('a', 101), "a", 0, null).Members.ShouldBe(new[] { "name" });
        Brand.Validate("a", new string('a', 101), 0, null).Members.ShouldBe(new[] { "slug" });
    }

    [Fact]
    public void All_errors_are_reported_together()
    {
        Brand.Validate("", "-", 10_000, new string('k', 201)).Members
            .ShouldBe(new[] { "name", "slug", "displayOrder", "logoKey" }, ignoreOrder: true);
    }
}
