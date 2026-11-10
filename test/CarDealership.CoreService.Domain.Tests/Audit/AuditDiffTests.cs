using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Audit;

/// <summary>Diff thuần: diff rỗng không ghi, tạo/xóa, trường nhạy cảm chỉ ghi tên (Req 17.1, 17.2, 17.5).</summary>
public class AuditDiffTests
{
    private static Dictionary<string, object?> Snap(params (string Key, object? Value)[] fields) =>
        fields.ToDictionary(f => f.Key, f => f.Value);

    [Fact]
    public void Identical_snapshots_produce_an_empty_diff()
    {
        var before = Snap(("name", "Toyota"), ("order", 1));
        var after = Snap(("name", "Toyota"), ("order", 1));

        AuditDiff.Compute(before, after).ShouldBeEmpty();
    }

    [Fact]
    public void Only_changed_fields_are_reported_with_before_and_after()
    {
        var before = Snap(("name", "Toyota"), ("order", 1));
        var after = Snap(("name", "Toyota Vietnam"), ("order", 1));

        var changes = AuditDiff.Compute(before, after);

        changes.Count.ShouldBe(1);
        changes[0].Field.ShouldBe("name");
        changes[0].Before!.GetValue<string>().ShouldBe("Toyota");
        changes[0].After!.GetValue<string>().ShouldBe("Toyota Vietnam");
        changes[0].Sensitive.ShouldBeFalse();
    }

    [Fact]
    public void Create_has_empty_before_and_skips_null_fields()
    {
        var after = Snap(("name", "Vios"), ("description", null), ("order", 0));

        var changes = AuditDiff.Compute(null, after);

        changes.Select(c => c.Field).ShouldBe(new[] { "name", "order" });
        changes.ShouldAllBe(c => c.Before == null);
    }

    [Fact]
    public void Delete_has_empty_after()
    {
        var before = Snap(("name", "Vios"), ("order", 3));

        var changes = AuditDiff.Compute(before, null);

        changes.Select(c => c.Field).ShouldBe(new[] { "name", "order" });
        changes.ShouldAllBe(c => c.After == null);
    }

    [Fact]
    public void Two_empty_snapshots_produce_an_empty_diff()
    {
        AuditDiff.Compute(null, null).ShouldBeEmpty();
        AuditDiff.Compute(Snap(), Snap()).ShouldBeEmpty();
    }

    [Fact]
    public void Enums_are_written_as_names_and_compared_by_value()
    {
        var changes = AuditDiff.Compute(
            Snap(("status", System.DayOfWeek.Monday)),
            Snap(("status", System.DayOfWeek.Tuesday)));

        changes.Single().Before!.GetValue<string>().ShouldBe("Monday");
        changes.Single().After!.GetValue<string>().ShouldBe("Tuesday");
    }

    [Fact]
    public void Collections_are_compared_by_content()
    {
        var same = AuditDiff.Compute(
            Snap(("tags", new[] { "a", "b" })),
            Snap(("tags", new List<string> { "a", "b" })));
        var different = AuditDiff.Compute(
            Snap(("tags", new[] { "a", "b" })),
            Snap(("tags", new[] { "a", "c" })));

        same.ShouldBeEmpty();
        different.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("passwordHash")]
    [InlineData("securityStamp")]
    [InlineData("token")]
    [InlineData("secret")]
    [InlineData("PASSWORD")]
    [InlineData("PasswordHash")]
    public void Sensitive_fields_carry_only_their_name(string field)
    {
        var changes = AuditDiff.Compute(Snap((field, "old-value-123")), Snap((field, "new-value-456")));

        changes.Count.ShouldBe(1);
        changes[0].Sensitive.ShouldBeTrue();
        changes[0].Before.ShouldBeNull();
        changes[0].After.ShouldBeNull();

        var json = AuditDiff.ToJson(changes);
        json.ShouldNotContain("old-value-123");
        json.ShouldNotContain("new-value-456");
        using var doc = JsonDocument.Parse(json);
        var item = doc.RootElement[0];
        item.GetProperty("field").GetString().ShouldBe(field);
        item.TryGetProperty("before", out _).ShouldBeFalse();
        item.TryGetProperty("after", out _).ShouldBeFalse();
    }

    [Fact]
    public void Unchanged_sensitive_field_is_not_reported()
    {
        AuditDiff.Compute(Snap(("password", "same")), Snap(("password", "same"))).ShouldBeEmpty();
    }

    [Fact]
    public void Json_is_an_array_of_field_before_after()
    {
        var changes = AuditDiff.Compute(Snap(("name", "A"), ("order", 1)), Snap(("name", "B"), ("order", 1)));

        using var doc = JsonDocument.Parse(AuditDiff.ToJson(changes));

        doc.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
        var item = doc.RootElement[0];
        item.GetProperty("field").GetString().ShouldBe("name");
        item.GetProperty("before").GetString().ShouldBe("A");
        item.GetProperty("after").GetString().ShouldBe("B");
    }

    [Fact]
    public void Json_of_a_create_has_null_before()
    {
        using var doc = JsonDocument.Parse(AuditDiff.ToJson(AuditDiff.Compute(null, Snap(("name", "A")))));

        doc.RootElement[0].GetProperty("before").ValueKind.ShouldBe(JsonValueKind.Null);
        doc.RootElement[0].GetProperty("after").GetString().ShouldBe("A");
    }
}
