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
/// Vòng đời phiên = family refresh token trên PostgreSQL thật.
/// Đồng hồ là <see cref="TestClock"/> nên các mốc 3 ngày / 7 ngày / 20 giây chạy tức thì.
/// </summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class SessionLifecycleTests : CoreServiceEntityFrameworkCoreTestBase, IDisposable
{
    private const string GoodPassword = "Good-Passw0rd!2026";
    private const string WellFormedUnknown = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

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

    private async Task<LoginResultDto> LoginAsync(string email, string password = GoodPassword)
    {
        var result = await WithUnitOfWorkAsync(() => Sessions.LoginAsync(new LoginInput { Email = email, Password = password }));
        result.ShouldNotBeNull();
        return result;
    }

    private Task<LoginResultDto?> RefreshAsync(string? token) =>
        WithUnitOfWorkAsync(() => Sessions.RefreshAsync(token));

    private Task<bool> RevokeAsync(string? token) =>
        WithUnitOfWorkAsync(() => Sessions.RevokeAsync(token));

    private Task<CurrentSessionDto?> CurrentAsync(LoginResultDto login) =>
        WithUnitOfWorkAsync(() => Sessions.GetCurrentAsync(login.SessionId, login.UserId));

    private Task<AdminSession> RowAsync(Guid sessionId) =>
        WithDbAsync(db => db.AdminSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId));

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

    // ---------- đăng nhập + tra phiên theo claim ----------

    [Fact]
    public async Task Login_returns_a_refresh_token_session_id_and_7_day_expiry()
    {
        var (email, userId) = await CreateUserAsync();

        var login = await LoginAsync(email);

        SessionToken.IsWellFormed(login.RefreshToken).ShouldBeTrue();
        login.SessionId.ShouldNotBe(Guid.Empty);
        login.UserId.ShouldBe(userId);
        login.Permissions.ShouldBe(new[] { "Leads.Manage", "Leads.View" });
        (login.SessionExpiresAt - (await RowAsync(login.SessionId)).CreatedAt).ShouldBe(TimeSpan.FromDays(7));
    }

    [Fact]
    public async Task Current_by_sid_and_sub_returns_identity_roles_and_permissions_and_writes_nothing()
    {
        var (email, userId) = await CreateUserAsync();
        var login = await LoginAsync(email);
        var before = await RowAsync(login.SessionId);

        Clock.Advance(TimeSpan.FromMinutes(5));
        var current = await CurrentAsync(login);

        current.ShouldNotBeNull();
        current.SessionId.ShouldBe(login.SessionId);
        current.UserId.ShouldBe(userId);
        current.Email.ShouldBe(email);
        current.Roles.ShouldBe(new[] { "SalesStaff" });
        current.Permissions.ShouldBe(new[] { "Leads.Manage", "Leads.View" });
        (await RowAsync(login.SessionId)).LastSeenAt.ShouldBe(before.LastSeenAt);   // tra mỗi request không ghi DB
    }

    [Fact]
    public async Task Current_with_a_sid_of_another_user_or_missing_claims_is_refused()
    {
        var (email, _) = await CreateUserAsync();
        var login = await LoginAsync(email);

        (await WithUnitOfWorkAsync(() => Sessions.GetCurrentAsync(login.SessionId, Guid.NewGuid()))).ShouldBeNull();
        (await WithUnitOfWorkAsync(() => Sessions.GetCurrentAsync(Guid.NewGuid(), login.UserId))).ShouldBeNull();
        (await WithUnitOfWorkAsync(() => Sessions.GetCurrentAsync(null, login.UserId))).ShouldBeNull();
        (await WithUnitOfWorkAsync(() => Sessions.GetCurrentAsync(login.SessionId, null))).ShouldBeNull();
    }

    // ---------- xoay vòng ----------

    [Fact]
    public async Task Refresh_rotates_the_token_and_slides_the_idle_window()
    {
        var (email, _) = await CreateUserAsync();
        var login = await LoginAsync(email);

        Clock.Advance(TimeSpan.FromMinutes(10));
        var refreshed = await RefreshAsync(login.RefreshToken);

        refreshed.ShouldNotBeNull();
        refreshed.RefreshToken.ShouldNotBeNull();
        refreshed.RefreshToken.ShouldNotBe(login.RefreshToken);
        refreshed.SessionId.ShouldBe(login.SessionId);
        refreshed.SessionExpiresAt.ShouldBe(login.SessionExpiresAt);   // tuyệt đối KHÔNG trượt

        var row = await RowAsync(login.SessionId);
        row.TokenHash.ShouldBe(SessionToken.Hash(refreshed.RefreshToken));
        row.PreviousTokenHash.ShouldBe(SessionToken.Hash(login.RefreshToken!));
        (row.LastSeenAt - row.CreatedAt).ShouldBeGreaterThan(TimeSpan.FromMinutes(9));
    }

    [Fact]
    public async Task Previous_token_inside_the_20_second_grace_is_accepted_without_rotating()
    {
        var (email, _) = await CreateUserAsync();
        var login = await LoginAsync(email);
        var rotated = await RefreshAsync(login.RefreshToken);
        var before = await RowAsync(login.SessionId);

        Clock.Advance(TimeSpan.FromSeconds(10));
        var again = await RefreshAsync(login.RefreshToken);   // tab thứ hai trình token cũ

        again.ShouldNotBeNull();
        again.RefreshToken.ShouldBeNull();                     // không phát token mới: BFF giữ nguyên cookie
        again.SessionId.ShouldBe(login.SessionId);
        var after = await RowAsync(login.SessionId);
        after.TokenHash.ShouldBe(before.TokenHash);
        after.RevokedAt.ShouldBeNull();
        (await RefreshAsync(rotated!.RefreshToken)).ShouldNotBeNull();   // token hiện hành vẫn dùng được
    }

    [Fact]
    public async Task Reusing_a_rotated_token_after_the_grace_revokes_the_whole_family()
    {
        var (email, userId) = await CreateUserAsync();
        var login = await LoginAsync(email);
        var rotated = await RefreshAsync(login.RefreshToken);

        Clock.Advance(TimeSpan.FromSeconds(25));
        (await RefreshAsync(login.RefreshToken)).ShouldBeNull();   // kẻ gian (hoặc chủ thật) dùng lại token cũ

        var row = await RowAsync(login.SessionId);
        row.RevokedAt.ShouldNotBeNull();
        row.RevokeReason.ShouldBe(SessionRevokeReasons.RefreshTokenReuse);
        (await RefreshAsync(rotated!.RefreshToken)).ShouldBeNull();   // token "hợp lệ" cũng chết theo
        (await CurrentAsync(login)).ShouldBeNull();                   // access token còn hạn bị từ chối ngay

        var reuse = (await SecurityLogsAsync()).Where(l => l.Action == SecurityEventActions.RefreshTokenReuse).ToList();
        reuse.Count.ShouldBe(1);
        reuse[0].UserId.ShouldBe(userId);
    }

    [Fact]
    public async Task Two_concurrent_refreshes_with_the_same_token_both_succeed_and_rotate_exactly_once()
    {
        var (email, _) = await CreateUserAsync();
        var login = await LoginAsync(email);

        var results = await Task.WhenAll(RefreshAsync(login.RefreshToken), RefreshAsync(login.RefreshToken));

        results.ShouldAllBe(r => r != null);
        results.Count(r => r!.RefreshToken is not null).ShouldBe(1);
        var issued = results.Single(r => r!.RefreshToken is not null)!.RefreshToken!;
        (await RowAsync(login.SessionId)).TokenHash.ShouldBe(SessionToken.Hash(issued));
    }

    // ---------- idle 3 ngày, tuyệt đối 7 ngày ----------

    [Theory]
    [InlineData(3 * 86400 - 5, true)]
    [InlineData(3 * 86400, false)]
    [InlineData(3 * 86400 + 1, false)]
    public async Task Refresh_after_3_days_idle_is_refused(int idleSeconds, bool expectedUsable)
    {
        var (email, _) = await CreateUserAsync();
        var login = await LoginAsync(email);

        // Dự phòng 5 giây cho thời gian thật trôi (mốc chính xác nằm ở SessionLifetimeTests, đồng hồ thuần).
        Clock.Advance(TimeSpan.FromSeconds(idleSeconds));
        var refreshed = await RefreshAsync(login.RefreshToken);

        (refreshed is not null).ShouldBe(expectedUsable);
    }

    [Fact]
    public async Task Continuous_use_ends_7_days_after_login()
    {
        var (email, _) = await CreateUserAsync();
        var login = await LoginAsync(email);
        var token = login.RefreshToken;

        for (var day = 0; day < 6; day++)
        {
            Clock.Advance(TimeSpan.FromDays(1));
            var refreshed = await RefreshAsync(token);
            refreshed.ShouldNotBeNull();
            token = refreshed.RefreshToken;
        }

        Clock.Advance(TimeSpan.FromDays(1));   // đủ 7 ngày từ lúc đăng nhập
        (await RefreshAsync(token)).ShouldBeNull();
        (await CurrentAsync(login)).ShouldBeNull();
    }

    [Fact]
    public async Task Expired_refresh_writes_nothing()
    {
        var (email, _) = await CreateUserAsync();
        var login = await LoginAsync(email);
        var before = await RowAsync(login.SessionId);

        Clock.Advance(TimeSpan.FromDays(4));
        (await RefreshAsync(login.RefreshToken)).ShouldBeNull();

        var after = await RowAsync(login.SessionId);
        after.TokenHash.ShouldBe(before.TokenHash);
        after.LastSeenAt.ShouldBe(before.LastSeenAt);
        after.RevokedAt.ShouldBeNull();
    }

    // ---------- token rác ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rac")]
    [InlineData("'; DROP TABLE idn_admin_sessions; --")]
    [InlineData(WellFormedUnknown)]
    public async Task Missing_malformed_or_unknown_tokens_are_rejected(string? token)
    {
        (await RefreshAsync(token)).ShouldBeNull();
        (await RevokeAsync(token)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_stored_hash_is_not_accepted_as_a_refresh_token()
    {
        var (email, _) = await CreateUserAsync();
        var login = await LoginAsync(email);
        var hashAsText = Convert.ToBase64String((await RowAsync(login.SessionId)).TokenHash)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        (await RefreshAsync(hashAsText)).ShouldBeNull();
    }

    // ---------- đăng xuất ----------

    [Fact]
    public async Task Revoke_ends_the_session_immediately_for_refresh_and_for_live_access_tokens()
    {
        var (email, _) = await CreateUserAsync();
        var login = await LoginAsync(email);

        (await RevokeAsync(login.RefreshToken)).ShouldBeTrue();

        var row = await RowAsync(login.SessionId);
        row.RevokedAt.ShouldNotBeNull();
        row.RevokeReason.ShouldBe(SessionRevokeReasons.Logout);
        (await RefreshAsync(login.RefreshToken)).ShouldBeNull();   // cookie cũ gửi lại bị từ chối
        (await CurrentAsync(login)).ShouldBeNull();                // access token chưa hết 10 phút cũng bị từ chối
        (await RevokeAsync(login.RefreshToken)).ShouldBeFalse();   // lần hai: không còn gì để thu hồi
    }

    [Fact]
    public async Task Revoke_with_the_token_just_replaced_inside_the_grace_also_logs_out()
    {
        var (email, _) = await CreateUserAsync();
        var login = await LoginAsync(email);
        await RefreshAsync(login.RefreshToken);

        (await RevokeAsync(login.RefreshToken)).ShouldBeTrue();

        (await RowAsync(login.SessionId)).RevokedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Revoke_only_affects_that_session()
    {
        var (email, _) = await CreateUserAsync();
        var (otherEmail, _) = await CreateUserAsync();
        var first = await LoginAsync(email);
        var second = await LoginAsync(email);
        var other = await LoginAsync(otherEmail);

        await RevokeAsync(first.RefreshToken);

        (await CurrentAsync(first)).ShouldBeNull();
        (await CurrentAsync(second)).ShouldNotBeNull();
        (await CurrentAsync(other)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Logout_is_recorded_without_the_refresh_token_and_failed_logout_records_nothing()
    {
        var (email, userId) = await CreateUserAsync();
        var login = await LoginAsync(email);

        await RevokeAsync(WellFormedUnknown);
        (await SecurityLogsAsync()).ShouldNotContain(l => l.Action == SecurityEventActions.Logout && l.UserId == userId);

        await RevokeAsync(login.RefreshToken);

        var logout = (await SecurityLogsAsync()).Where(l => l.Action == SecurityEventActions.Logout && l.UserId == userId).ToList();
        logout.Count.ShouldBe(1);
        logout[0].UserName.ShouldBe(email);

        var dump = await WithDbAsync(db => db.Database.SqlQuery<string>($"select t::text as \"Value\" from \"AbpSecurityLogs\" t").ToListAsync());
        dump.ShouldAllBe(row => !row.Contains(login.RefreshToken!));
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

        (await RowAsync(first.SessionId)).RevokeReason.ShouldBe(SessionRevokeReasons.UserInactive);
        (await RowAsync(second.SessionId)).RevokedAt.ShouldNotBeNull();
        (await RowAsync(other.SessionId)).RevokedAt.ShouldBeNull();

        (await CurrentAsync(first)).ShouldBeNull();
        (await RefreshAsync(second.RefreshToken)).ShouldBeNull();
        (await CurrentAsync(other)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Reactivating_the_user_does_not_resurrect_old_sessions()
    {
        var (email, userId) = await CreateUserAsync();
        var login = await LoginAsync(email);

        await SetActiveAsync(userId, false);
        await SetActiveAsync(userId, true);

        (await RefreshAsync(login.RefreshToken)).ShouldBeNull();
        (await LoginAsync(email)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Resolver_and_refresh_reject_and_revoke_even_if_the_deactivation_event_was_missed()
    {
        var (email, userId) = await CreateUserAsync();
        var viaCurrent = await LoginAsync(email);
        var viaRefresh = await LoginAsync(email);

        // Ghi thẳng bằng SQL: không phát sự kiện thực thể nào, mô phỏng đường vô hiệu hóa không qua ABP.
        await WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "update \"AbpUsers\" set \"IsActive\" = false where \"Id\" = {0}", userId));
        (await RowAsync(viaCurrent.SessionId)).RevokedAt.ShouldBeNull();

        (await CurrentAsync(viaCurrent)).ShouldBeNull();
        (await RowAsync(viaCurrent.SessionId)).RevokedAt.ShouldNotBeNull();
        (await RowAsync(viaRefresh.SessionId)).RevokedAt.ShouldNotBeNull();   // thu hồi MỌI phiên của user
        (await RefreshAsync(viaRefresh.RefreshToken)).ShouldBeNull();
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
    public async Task Controller_maps_refresh_revoke_current_to_200_204_and_one_generic_401()
    {
        var (email, _) = await CreateUserAsync();
        var login = await LoginAsync(email);
        var controller = ActivatorUtilities.CreateInstance<SessionController>(ServiceProvider);

        var refresh = await WithUnitOfWorkAsync(() => controller.RefreshAsync(new RefreshTokenInput { RefreshToken = login.RefreshToken! }));
        var rotated = refresh.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<LoginResultDto>();

        var revoke = await WithUnitOfWorkAsync(() => controller.RevokeAsync(new RefreshTokenInput { RefreshToken = rotated.RefreshToken! }));
        revoke.StatusCode.ShouldBe(204);

        var bodies = new List<SessionFailureDto>();
        foreach (var result in new[]
                 {
                     await WithUnitOfWorkAsync(() => controller.RefreshAsync(new RefreshTokenInput { RefreshToken = rotated.RefreshToken! })),  // đã đăng xuất
                     await WithUnitOfWorkAsync(() => controller.RevokeAsync(new RefreshTokenInput { RefreshToken = "rac" })),                   // sai dạng
                     await WithUnitOfWorkAsync(() => controller.GetCurrentAsync()),                                                            // không có claim
                 })
        {
            bodies.Add(result.ShouldBeOfType<UnauthorizedObjectResult>().Value.ShouldBeOfType<SessionFailureDto>());
        }

        bodies.Select(b => (b.Error, b.Message)).Distinct().Count().ShouldBe(1);
        bodies[0].Error.ShouldBe("unauthenticated");
    }
}
