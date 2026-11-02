using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Identity;
using CarDealership.CoreService.Identity.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Xunit;
using AbpIdentityUser = Volo.Abp.Identity.IdentityUser;

namespace CarDealership.CoreService.EntityFrameworkCore.Identity;

/// <summary>
/// Ngày 34 (Req 14.7, 14.8, 14.9, 17): vòng đời session trên PostgreSQL thật. Đồng hồ là <see cref="TestClock"/>
/// nên các mốc 29:59 / 30:00 / 30:01 chạy tức thì.
/// </summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class SessionLifecycleTests : CoreServiceEntityFrameworkCoreTestBase, IDisposable
{
    private const string GoodPassword = "Good-Passw0rd!2026";

    private TestClock Clock => GetRequiredService<TestClock>();

    public void Dispose() => Clock.Reset();

    private ISessionAppService Sessions => GetRequiredService<ISessionAppService>();

    private Task<T> WithDbAsync<T>(Func<CoreServiceDbContext, Task<T>> action) =>
        WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CoreServiceDbContext>>().GetDbContextAsync();
            return await action(db);
        });

    private async Task<(string Email, Guid UserId)> CreateUserAsync()
    {
        var email = $"user{Guid.NewGuid():N}"[..20] + "@example.com";
        var id = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            var userManager = GetRequiredService<IdentityUserManager>();
            var user = new AbpIdentityUser(id, email, email);
            (await userManager.CreateAsync(user, GoodPassword)).Succeeded.ShouldBeTrue();
            (await userManager.AddToRoleAsync(user, AppRoles.SalesStaff)).Succeeded.ShouldBeTrue();
        });
        return (email, id);
    }

    private async Task<string> LoginAsync(string email, string password = GoodPassword)
    {
        var result = await WithUnitOfWorkAsync(() => Sessions.LoginAsync(new LoginInput { Email = email, Password = password }));
        result.ShouldNotBeNull();
        return result.SessionId;
    }

    private Task<CurrentSessionDto?> CurrentAsync(string? sessionId) =>
        WithUnitOfWorkAsync(() => Sessions.GetCurrentAsync(sessionId));

    private Task<bool> LogoutAsync(string? sessionId) =>
        WithUnitOfWorkAsync(() => Sessions.LogoutAsync(sessionId));

    private Task<AdminSession> RowAsync(string sessionId)
    {
        var hash = SessionToken.Hash(sessionId);
        return WithDbAsync(db => db.AdminSessions.AsNoTracking().SingleAsync(s => s.TokenHash == hash));
    }

    private Task<List<IdentitySecurityLog>> SecurityLogsAsync() =>
        WithDbAsync(db => db.SecurityLogs.AsNoTracking().OrderBy(l => l.CreationTime).ToListAsync());

    private async Task SetActiveAsync(Guid userId, bool active)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var userManager = GetRequiredService<IdentityUserManager>();
            var user = (await userManager.FindByIdAsync(userId.ToString()))!;
            user.SetIsActive(active);
            (await userManager.UpdateAsync(user)).Succeeded.ShouldBeTrue();
        });
    }

    // ---------- tra session ----------

    [Fact]
    public async Task Valid_session_returns_identity_roles_and_permissions()
    {
        var (email, userId) = await CreateUserAsync();
        var session = await LoginAsync(email);

        var current = await CurrentAsync(session);

        current.ShouldNotBeNull();
        current.UserId.ShouldBe(userId);
        current.Email.ShouldBe(email);
        current.Roles.ShouldBe(new[] { "SalesStaff" });
        current.Permissions.ShouldBe(new[] { "Leads.Manage", "Leads.View" });
    }

    [Fact]
    public async Task Every_successful_lookup_slides_last_seen_to_now()
    {
        var (email, _) = await CreateUserAsync();
        var session = await LoginAsync(email);
        var created = (await RowAsync(session)).LastSeenAt;

        Clock.Advance(TimeSpan.FromMinutes(10));
        await CurrentAsync(session);

        var after = (await RowAsync(session)).LastSeenAt;
        (after - created).ShouldBeGreaterThan(TimeSpan.FromMinutes(9));
        (after - created).ShouldBeLessThan(TimeSpan.FromMinutes(11));
    }

    [Theory]
    [InlineData(29 * 60 + 55, true)]
    [InlineData(30 * 60, false)]
    [InlineData(30 * 60 + 1, false)]
    public async Task Session_idle_29_55_ok_and_30_00_30_01_expired(int idleSeconds, bool expectedUsable)
    {
        var (email, _) = await CreateUserAsync();
        var session = await LoginAsync(email);

        // Dự phòng 5 giây cho thời gian thật trôi giữa lúc đăng nhập và lúc tra (mốc 29:59 chính xác nằm ở SessionLifetimeTests, đồng hồ thuần).
        Clock.Advance(TimeSpan.FromSeconds(idleSeconds));
        var current = await CurrentAsync(session);

        if (expectedUsable)
        {
            current.ShouldNotBeNull();
        }
        else
        {
            current.ShouldBeNull();
        }
    }

    [Fact]
    public async Task Activity_keeps_the_session_alive_beyond_30_minutes_in_total()
    {
        var (email, _) = await CreateUserAsync();
        var session = await LoginAsync(email);

        for (var i = 0; i < 4; i++)
        {
            Clock.Advance(TimeSpan.FromMinutes(20));
            (await CurrentAsync(session)).ShouldNotBeNull();   // 80 phút tổng cộng, mỗi lần cách 20 phút
        }

        Clock.Advance(TimeSpan.FromMinutes(31));
        (await CurrentAsync(session)).ShouldBeNull();
    }

    [Fact]
    public async Task Expired_lookup_writes_nothing_and_stays_expired()
    {
        var (email, _) = await CreateUserAsync();
        var session = await LoginAsync(email);
        var before = await RowAsync(session);

        Clock.Advance(TimeSpan.FromMinutes(45));
        (await CurrentAsync(session)).ShouldBeNull();
        (await CurrentAsync(session)).ShouldBeNull();

        var after = await RowAsync(session);
        after.LastSeenAt.ShouldBe(before.LastSeenAt);
        after.RevokedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rac")]
    [InlineData("'; DROP TABLE idn_admin_sessions; --")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]     // đúng dạng nhưng không tồn tại
    public async Task Missing_malformed_or_unknown_session_ids_are_rejected(string? sessionId)
    {
        (await CurrentAsync(sessionId)).ShouldBeNull();
        (await LogoutAsync(sessionId)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_stored_hash_is_not_accepted_as_a_session_id()
    {
        var (email, _) = await CreateUserAsync();
        var session = await LoginAsync(email);
        var hashAsText = Convert.ToBase64String((await RowAsync(session)).TokenHash).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        (await CurrentAsync(hashAsText)).ShouldBeNull();
    }

    // ---------- đăng xuất ----------

    [Fact]
    public async Task Logout_revokes_the_session_and_the_old_session_id_is_refused_afterwards()
    {
        var (email, _) = await CreateUserAsync();
        var session = await LoginAsync(email);

        (await LogoutAsync(session)).ShouldBeTrue();

        (await RowAsync(session)).RevokedAt.ShouldNotBeNull();
        (await CurrentAsync(session)).ShouldBeNull();     // cookie cũ gửi lại bị từ chối (Req 14.8)
        (await LogoutAsync(session)).ShouldBeFalse();     // đăng xuất lần hai: không còn gì để thu hồi
    }

    [Fact]
    public async Task Logout_only_affects_that_session_not_other_sessions_of_the_same_or_other_users()
    {
        var (email, _) = await CreateUserAsync();
        var (otherEmail, _) = await CreateUserAsync();
        var first = await LoginAsync(email);
        var second = await LoginAsync(email);
        var other = await LoginAsync(otherEmail);

        await LogoutAsync(first);

        (await CurrentAsync(first)).ShouldBeNull();
        (await CurrentAsync(second)).ShouldNotBeNull();
        (await CurrentAsync(other)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Logout_is_recorded_as_a_security_event_without_the_session_id()
    {
        var (email, userId) = await CreateUserAsync();
        var session = await LoginAsync(email);

        await LogoutAsync(session);

        var logs = await SecurityLogsAsync();
        var logout = logs.Where(l => l.Action == SecurityEventActions.Logout).ToList();
        logout.Count.ShouldBe(1);
        logout[0].UserId.ShouldBe(userId);
        logout[0].UserName.ShouldBe(email);

        var dump = await WithDbAsync(db => db.Database.SqlQuery<string>($"select t::text as \"Value\" from \"AbpSecurityLogs\" t").ToListAsync());
        dump.ShouldAllBe(row => !row.Contains(session));
    }

    [Fact]
    public async Task Failed_logout_writes_no_security_event()
    {
        await LogoutAsync("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        (await SecurityLogsAsync()).ShouldNotContain(l => l.Action == SecurityEventActions.Logout);
    }

    // ---------- user Inactive ----------

    [Fact]
    public async Task Deactivating_a_user_revokes_all_of_that_users_sessions_and_only_theirs()
    {
        var (email, userId) = await CreateUserAsync();
        var (otherEmail, _) = await CreateUserAsync();
        var first = await LoginAsync(email);
        var second = await LoginAsync(email);
        var other = await LoginAsync(otherEmail);

        await SetActiveAsync(userId, false);

        // Sự kiện cập nhật user phải thu hồi ngay ở DB, không đợi tới lần tra kế tiếp.
        (await RowAsync(first)).RevokedAt.ShouldNotBeNull();
        (await RowAsync(second)).RevokedAt.ShouldNotBeNull();
        (await RowAsync(other)).RevokedAt.ShouldBeNull();

        (await CurrentAsync(first)).ShouldBeNull();
        (await CurrentAsync(second)).ShouldBeNull();
        (await CurrentAsync(other)).ShouldNotBeNull();

        var revoked = (await SecurityLogsAsync()).Where(l => l.Action == SecurityEventActions.SessionsRevoked).ToList();
        revoked.Count.ShouldBe(1);
        revoked[0].UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task Reactivating_the_user_does_not_resurrect_old_sessions()
    {
        var (email, userId) = await CreateUserAsync();
        var session = await LoginAsync(email);

        await SetActiveAsync(userId, false);
        await SetActiveAsync(userId, true);

        (await CurrentAsync(session)).ShouldBeNull();
        (await LoginAsync(email)).ShouldNotBeNull();      // đăng nhập lại bình thường
    }

    [Fact]
    public async Task Resolver_alone_rejects_and_revokes_even_if_the_deactivation_event_was_missed()
    {
        var (email, userId) = await CreateUserAsync();
        var session = await LoginAsync(email);

        // Ghi thẳng vào DB bằng SQL: không phát sự kiện thực thể nào, mô phỏng đường vô hiệu hóa không qua ABP.
        await WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "update \"AbpUsers\" set \"IsActive\" = false where \"Id\" = {0}", userId));

        (await RowAsync(session)).RevokedAt.ShouldBeNull();   // chưa ai thu hồi

        (await CurrentAsync(session)).ShouldBeNull();          // nhưng request kế tiếp bị từ chối

        (await RowAsync(session)).RevokedAt.ShouldNotBeNull(); // và session được thu hồi luôn
    }

    [Fact]
    public async Task Inactive_user_cannot_log_in_even_with_the_right_password()
    {
        var (email, userId) = await CreateUserAsync();
        await SetActiveAsync(userId, false);

        var result = await WithUnitOfWorkAsync(() => Sessions.LoginAsync(new LoginInput { Email = email, Password = GoodPassword }));

        result.ShouldBeNull();
    }

    // ---------- controller ----------

    [Fact]
    public async Task Controller_maps_current_and_logout_to_200_204_and_one_generic_401()
    {
        var (email, _) = await CreateUserAsync();
        var session = await LoginAsync(email);
        var controller = ActivatorUtilities.CreateInstance<SessionController>(ServiceProvider);

        var current = await WithUnitOfWorkAsync(() => controller.GetCurrentAsync(session));
        current.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<CurrentSessionDto>();

        var logout = await WithUnitOfWorkAsync(() => controller.LogoutAsync(session));
        logout.StatusCode.ShouldBe(204);

        var bodies = new List<SessionFailureDto>();
        foreach (var result in new[]
                 {
                     await WithUnitOfWorkAsync(() => controller.GetCurrentAsync(session)),          // đã đăng xuất
                     await WithUnitOfWorkAsync(() => controller.GetCurrentAsync(null)),             // thiếu header
                     await WithUnitOfWorkAsync(() => controller.LogoutAsync("rac")),                // sai dạng
                 })
        {
            bodies.Add(result.ShouldBeOfType<UnauthorizedObjectResult>().Value.ShouldBeOfType<SessionFailureDto>());
        }

        bodies.Select(b => (b.Error, b.Message)).Distinct().Count().ShouldBe(1);
        bodies[0].Error.ShouldBe("unauthenticated");
    }
}
