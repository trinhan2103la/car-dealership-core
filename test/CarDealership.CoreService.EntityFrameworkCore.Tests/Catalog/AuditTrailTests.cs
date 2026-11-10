using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CarDealership.CoreService.Audit;
using CarDealership.CoreService.Identity;
using Shouldly;
using Volo.Abp.Uow;
using Xunit;

namespace CarDealership.CoreService.Catalog;

/// <summary>IAuditTrail: cùng UoW với nghiệp vụ (Req 17.3, 17.4) và security event ở UoW riêng (Req 17.10).</summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class AuditTrailTests : CatalogTestBase
{
    private IAuditTrail Trail => GetRequiredService<AuditTrail>();

    private static Dictionary<string, object?> Snap(string name) => new() { ["name"] = name };

    [Fact]
    public async Task Business_audit_is_lost_when_the_unit_of_work_is_not_completed()
    {
        using (var scope = GetRequiredService<IUnitOfWorkManager>().Begin(new AbpUnitOfWorkOptions { IsTransactional = true }))
        {
            await Trail.WriteAsync(AuditActions.Update, "Thing", "t1", Snap("a"), Snap("b"));
            // không CompleteAsync: rollback
        }

        (await AuditEntriesAsync("t1")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Empty_diff_writes_nothing_and_returns_false()
    {
        var written = await AsAdminAsync(() => Trail.WriteAsync(AuditActions.Update, "Thing", "t2", Snap("a"), Snap("a")));

        written.ShouldBeFalse();
        (await AuditEntriesAsync("t2")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Sensitive_values_never_reach_the_table()
    {
        await AsAdminAsync(() => Trail.WriteAsync(
            AuditActions.Update, "Thing", "t3",
            new Dictionary<string, object?> { ["passwordHash"] = "old-hash-value", ["name"] = "a" },
            new Dictionary<string, object?> { ["passwordHash"] = "new-hash-value", ["name"] = "b" }));

        var entry = (await AuditEntriesAsync("t3")).Single();
        entry.Changes.ShouldNotContain("hash-value");
        using var doc = JsonDocument.Parse(entry.Changes);
        doc.RootElement.EnumerateArray().Select(c => c.GetProperty("field").GetString()).ShouldBe(new[] { "passwordHash", "name" });
    }

    [Fact]
    public async Task Entry_carries_actor_and_the_correlation_id_of_the_request()
    {
        await AsAdminAsync(() => Trail.WriteAsync(AuditActions.Create, "Thing", "t4", null, Snap("a")), correlationId: "req-abc_123");

        var entry = (await AuditEntriesAsync("t4")).Single();
        entry.ActorId.ShouldBe(ActorId);
        entry.CorrelationId.ShouldBe("req-abc_123");
    }

    [Fact]
    public async Task Security_event_survives_a_rolled_back_outer_unit_of_work()
    {
        var userId = Guid.NewGuid();

        using (GetRequiredService<IUnitOfWorkManager>().Begin(new AbpUnitOfWorkOptions { IsTransactional = true }))
        {
            await Trail.WriteSecurityEventAsync(SecurityEventActions.LoginFailed, userId, LoginFailureReasons.InvalidPassword);
            // UoW ngoài không hoàn tất (giống đăng nhập sai): security event vẫn phải còn.
        }

        var entry = (await AuditEntriesAsync(userId.ToString())).Single();
        entry.Action.ShouldBe(SecurityEventActions.LoginFailed);
        entry.EntityType.ShouldBe(AuditTrail.SecurityEntityType);
        entry.ActorId.ShouldBe(userId);
        entry.Changes.ShouldContain(LoginFailureReasons.InvalidPassword);
    }

    [Fact]
    public async Task Security_event_for_an_unknown_user_is_recorded_without_actor()
    {
        await AsAdminAsync(() => Trail.WriteSecurityEventAsync(SecurityEventActions.LoginFailed, null, LoginFailureReasons.UnknownUser));

        var entry = (await AuditEntriesAsync(AuditTrail.SecurityEntityIdWhenUnknown)).Single();
        entry.ActorId.ShouldBeNull();
    }
}
