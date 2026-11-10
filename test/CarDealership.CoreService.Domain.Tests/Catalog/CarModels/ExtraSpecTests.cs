using System.Linq;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Catalog.CarModels;

public class ExtraSpecTests
{
    private static ExtraSpecInput Item(string? group = "G", string? name = "N", string? value = "V", string? unit = null) =>
        new(group, name, value, unit);

    [Fact]
    public void Values_are_trimmed_and_blank_unit_becomes_null()
    {
        var errors = new FieldErrors();

        var result = ExtraSpec.Normalize(new[] { Item(" Kích thước ", " Dài ", " 4425 ", "  ") }, errors);

        errors.Any.ShouldBeFalse();
        result.Single().ShouldBe(new ExtraSpec("Kích thước", "Dài", "4425", null));
    }

    [Fact]
    public void Two_hundred_is_allowed_and_two_hundred_one_is_rejected()
    {
        var ok = new FieldErrors();
        ExtraSpec.Normalize(Enumerable.Range(0, 200).Select(i => Item(name: $"n{i}")).ToList(), ok);
        ok.Any.ShouldBeFalse();

        var tooMany = new FieldErrors();
        ExtraSpec.Normalize(Enumerable.Range(0, 201).Select(i => Item(name: $"n{i}")).ToList(), tooMany);
        tooMany.Members.ShouldBe(new[] { "extraSpecs" });
    }

    [Fact]
    public void Errors_are_reported_per_index_and_field()
    {
        var errors = new FieldErrors();

        ExtraSpec.Normalize(new[]
        {
            Item(),
            Item(group: "", name: new string('n', 101), value: new string('v', 256), unit: new string('u', 21)),
        }, errors);

        errors.Members.ShouldBe(new[] { "extraSpecs[1].group", "extraSpecs[1].name", "extraSpecs[1].value", "extraSpecs[1].unit" }, ignoreOrder: true);
    }

    [Fact]
    public void Name_must_be_unique_within_a_group_ignoring_case()
    {
        var errors = new FieldErrors();

        ExtraSpec.Normalize(new[] { Item("G", "Dài"), Item("g", "DÀI"), Item("Other", "Dài") }, errors);

        errors.Members.ShouldBe(new[] { "extraSpecs[1].name" });
    }
}
