using System;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Audit;

public class AuditEntryTests
{
    private static readonly DateTime T0 = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);

    private static AuditEntry Valid(
        DateTime? at = null, Guid? actor = null, string action = "Create", string type = "Brand",
        string id = "b1", string cid = "cid-1", string? changes = null) =>
        new(Guid.NewGuid(), at ?? T0, actor, action, type, id, cid, changes);

    [Fact]
    public void Valid_entry_keeps_values_and_defaults_changes_to_empty_array()
    {
        var actor = Guid.NewGuid();
        var entry = Valid(actor: actor);

        entry.ActorId.ShouldBe(actor);
        entry.Action.ShouldBe("Create");
        entry.OccurredAt.ShouldBe(T0);
        entry.Changes.ShouldBe(AuditEntry.EmptyChanges);
    }

    [Fact]
    public void Non_utc_time_is_rejected()
    {
        Should.Throw<ArgumentException>(() => Valid(at: DateTime.SpecifyKind(T0, DateTimeKind.Local)));
        Should.Throw<ArgumentException>(() => Valid(at: DateTime.SpecifyKind(T0, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void Empty_actor_guid_is_rejected_use_null_instead()
    {
        Should.Throw<ArgumentException>(() => Valid(actor: Guid.Empty));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_required_fields_are_rejected(string blank)
    {
        Should.Throw<ArgumentException>(() => Valid(action: blank));
        Should.Throw<ArgumentException>(() => Valid(type: blank));
        Should.Throw<ArgumentException>(() => Valid(id: blank));
        Should.Throw<ArgumentException>(() => Valid(cid: blank));
    }

    [Fact]
    public void Length_boundaries()
    {
        Valid(action: new string('a', AuditEntry.MaxActionLength)).ShouldNotBeNull();
        Should.Throw<ArgumentException>(() => Valid(action: new string('a', AuditEntry.MaxActionLength + 1)));
        Valid(cid: new string('c', AuditEntry.MaxCorrelationIdLength)).ShouldNotBeNull();
        Should.Throw<ArgumentException>(() => Valid(cid: new string('c', AuditEntry.MaxCorrelationIdLength + 1)));
    }

    [Fact]
    public void Time_is_truncated_to_microseconds_to_match_postgres()
    {
        var entry = Valid(at: T0.AddTicks(7));

        entry.OccurredAt.ShouldBe(T0);
    }

    [Fact]
    public void Entity_has_no_public_setters()
    {
        foreach (var property in typeof(AuditEntry).GetProperties())
        {
            if (property.Name == nameof(AuditEntry.Id))
            {
                continue;
            }

            (property.SetMethod?.IsPublic ?? false).ShouldBeFalse($"{property.Name} must not have a public setter");
        }
    }
}
