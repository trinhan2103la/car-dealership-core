using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Identity;
using CarDealership.CoreService.Identity.Login;
using CarDealership.CoreService.Identity.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Auditing;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Xunit;
using AbpIdentityUser = Volo.Abp.Identity.IdentityUser;

namespace CarDealership.CoreService.EntityFrameworkCore.Identity;

/// <summary>
/// Ngày 33 (Req 14.1, 14.4, 14.5, 14.6, 14.9, 17): đăng nhập trên PostgreSQL thật, qua ISessionAppService trong một
/// unit of work bao ngoài (giống request thật) để chứng minh lần sai VẪN được ghi dù kết quả là thất bại.
/// Đồng hồ là <see cref="TestClock"/>: dịch 14:59 / 15:00 mà không phải chờ.
/// </summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class LoginTests : CoreServiceEntityFrameworkCoreTestBase, IDisposable
{
    private const string GoodPassword = "Good-Passw0rd!2026";
    private const string BadPassword = "Wrong-Passw0rd!2026";

    private TestClock Clock => GetRequiredService<TestClock>();

    public void Dispose() => Clock.Reset();

    private Task<LoginResultDto?> LoginAsync(string email, string password) =>
        WithUnitOfWorkAsync(() => GetRequiredService<ISessionAppService>().LoginAsync(
            new LoginInput { Email = email, Password = password }));

    private async Task<string> CreateUserAsync(bool active = true, string role = AppRoles.SalesStaff)
    {
        var email = $"user{Guid.NewGuid():N}"[..20] + "@example.com";
        await WithUnitOfWorkAsync(async () =>
        {
            var userManager = GetRequiredService<IdentityUserManager>();
            var user = new AbpIdentityUser(Guid.NewGuid(), email, email);
            user.SetIsActive(active);
            (await userManager.CreateAsync(user, GoodPassword)).Succeeded.ShouldBeTrue();
            (await userManager.AddToRoleAsync(user, role)).Succeeded.ShouldBeTrue();
        });
        return email;
    }

    private Task<T> WithDbAsync<T>(Func<CoreServiceDbContext, Task<T>> action) =>
        WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CoreServiceDbContext>>().GetDbContextAsync();
            return await action(db);
        });

    private Task<List<IdentitySecurityLog>> SecurityLogsAsync() =>
        WithDbAsync(db => db.SecurityLogs.AsNoTracking().OrderBy(l => l.CreationTime).ToListAsync());

    private static string? Reason(IdentitySecurityLog log) =>
        log.ExtraProperties.TryGetValue("reason", out var value) ? value?.ToString() : null;

    [Fact]
    public void Test_clock_replaces_the_abp_clock_and_is_utc()
    {
        // Nếu IClock không phải TestClock thì mọi test dịch giờ bên dưới vô nghĩa.
        GetRequiredService<Volo.Abp.Timing.IClock>().ShouldBeSameAs(Clock);
        Clock.Now.Kind.ShouldBe(DateTimeKind.Utc);

        var before = Clock.Now;
        Clock.Advance(TimeSpan.FromMinutes(20));
        (Clock.Now - before).ShouldBeGreaterThan(TimeSpan.FromMinutes(19));
    }

    // ---------- thành công ----------

    [Fact]
    public async Task Correct_credentials_create_a_session_and_return_roles_and_permissions()
    {
        var result = await LoginAsync(CoreServiceTestConsts.SuperAdminEmail, CoreServiceTestConsts.SuperAdminPassword);

        result.ShouldNotBeNull();
        result.SessionId.Length.ShouldBe(43);
        result.Email.ShouldBe(CoreServiceTestConsts.SuperAdminEmail);
        result.Roles.ShouldBe(new[] { "SuperAdmin" });
        result.Permissions.Count.ShouldBe(9);
        result.Permissions.ShouldContain("Users.Manage");
    }

    [Fact]
    public async Task Permissions_follow_the_role_of_the_user()
    {
        var email = await CreateUserAsync(role: AppRoles.Viewer);

        var result = await LoginAsync(email, GoodPassword);

        result.ShouldNotBeNull();
        result.Roles.ShouldBe(new[] { "Viewer" });
        result.Permissions.ShouldBe(new[] { "Analytics.View" });
    }

    [Theory]
    [InlineData("  SUPERADMIN@EXAMPLE.COM  ")]
    [InlineData("SuperAdmin@Example.com")]
    [InlineData("\tsuperadmin@example.com\n")]
    public async Task Email_is_matched_case_insensitively_after_trim(string typed)
    {
        var result = await LoginAsync(typed, CoreServiceTestConsts.SuperAdminPassword);

        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task Session_row_stores_only_the_hash_of_the_returned_session_id()
    {
        var result = await LoginAsync(CoreServiceTestConsts.SuperAdminEmail, CoreServiceTestConsts.SuperAdminPassword);
        var hash = SessionToken.Hash(result!.SessionId);

        var sessions = await WithDbAsync(db => db.AdminSessions.AsNoTracking().ToListAsync());

        sessions.Count.ShouldBe(1);
        sessions[0].TokenHash.ShouldBe(hash);
        sessions[0].UserId.ShouldBe(result.UserId);
        sessions[0].RevokedAt.ShouldBeNull();

        var dump = await WithDbAsync(db => db.Database
            .SqlQuery<string>($"select t::text as \"Value\" from idn_admin_sessions t").ToListAsync());
        dump.ShouldAllBe(row => !row.Contains(result.SessionId));
    }

    [Fact]
    public async Task Each_login_gets_a_different_session_id()
    {
        var first = await LoginAsync(CoreServiceTestConsts.SuperAdminEmail, CoreServiceTestConsts.SuperAdminPassword);
        var second = await LoginAsync(CoreServiceTestConsts.SuperAdminEmail, CoreServiceTestConsts.SuperAdminPassword);

        first!.SessionId.ShouldNotBe(second!.SessionId);
    }

    [Fact]
    public async Task Success_is_recorded_as_attempt_and_security_event()
    {
        await LoginAsync(CoreServiceTestConsts.SuperAdminEmail, CoreServiceTestConsts.SuperAdminPassword);

        var attempts = await WithDbAsync(db => db.LoginAttempts.AsNoTracking().ToListAsync());
        attempts.Count.ShouldBe(1);
        attempts[0].Succeeded.ShouldBeTrue();
        attempts[0].NormalizedEmail.ShouldBe(CoreServiceTestConsts.SuperAdminEmail.ToUpperInvariant());

        var logs = (await SecurityLogsAsync()).Where(l => l.Action.StartsWith("AdminLogin")).ToList();
        logs.Select(l => l.Action).ShouldBe(new[] { SecurityEventActions.LoginSucceeded });
        logs[0].UserName.ShouldBe(CoreServiceTestConsts.SuperAdminEmail);
    }

    // ---------- thất bại: mọi lý do cho cùng một kết quả ----------

    [Fact]
    public async Task Wrong_password_unknown_email_and_inactive_user_all_fail_the_same_way()
    {
        var inactive = await CreateUserAsync(active: false);

        var wrongPassword = await LoginAsync(CoreServiceTestConsts.SuperAdminEmail, BadPassword);
        var unknownEmail = await LoginAsync("nobody@example.com", GoodPassword);
        var inactiveCorrectPassword = await LoginAsync(inactive, GoodPassword);

        wrongPassword.ShouldBeNull();
        unknownEmail.ShouldBeNull();
        inactiveCorrectPassword.ShouldBeNull();
        (await WithDbAsync(db => db.AdminSessions.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Failure_reasons_are_only_in_the_internal_security_log()
    {
        var inactive = await CreateUserAsync(active: false);

        await LoginAsync(CoreServiceTestConsts.SuperAdminEmail, BadPassword);
        await LoginAsync("nobody@example.com", GoodPassword);
        await LoginAsync(inactive, GoodPassword);

        var failures = (await SecurityLogsAsync()).Where(l => l.Action == SecurityEventActions.LoginFailed).ToList();
        failures.Select(Reason).ShouldBe(new[]
        {
            LoginFailureReasons.InvalidPassword, LoginFailureReasons.UnknownUser, LoginFailureReasons.Inactive
        }, ignoreOrder: true);
    }

    [Fact]
    public async Task Failed_attempt_is_stored_even_though_the_call_failed_and_unknown_email_has_null_user()
    {
        await LoginAsync("nobody@example.com", GoodPassword);

        var attempts = await WithDbAsync(db => db.LoginAttempts.AsNoTracking().ToListAsync());
        attempts.Count.ShouldBe(1);
        attempts[0].Succeeded.ShouldBeFalse();
        attempts[0].UserId.ShouldBeNull();
        attempts[0].NormalizedEmail.ShouldBe("NOBODY@EXAMPLE.COM");
    }

    [Fact]
    public async Task Controller_returns_the_same_401_body_for_every_kind_of_failure_and_200_on_success()
    {
        var inactive = await CreateUserAsync(active: false);
        var controller = ActivatorUtilities.CreateInstance<SessionController>(ServiceProvider);

        async Task<IActionResult> Call(string email, string password) =>
            await WithUnitOfWorkAsync(() => controller.LoginAsync(new LoginInput { Email = email, Password = password }));

        var bodies = new List<LoginFailureDto>();
        foreach (var result in new[]
                 {
                     await Call(CoreServiceTestConsts.SuperAdminEmail, BadPassword),
                     await Call("nobody@example.com", GoodPassword),
                     await Call(inactive, GoodPassword),
                 })
        {
            var unauthorized = result.ShouldBeOfType<UnauthorizedObjectResult>();
            bodies.Add(unauthorized.Value.ShouldBeOfType<LoginFailureDto>());
        }

        bodies.Select(b => (b.Error, b.Message)).Distinct().Count().ShouldBe(1);
        bodies[0].Error.ShouldBe("invalid_credentials");

        var ok = await Call(CoreServiceTestConsts.SuperAdminEmail, CoreServiceTestConsts.SuperAdminPassword);
        ok.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<LoginResultDto>();
    }

    // ---------- khóa 5 lần / 15 phút ----------

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_correct_password()
    {
        var email = await CreateUserAsync();

        for (var i = 0; i < 5; i++)
        {
            (await LoginAsync(email, BadPassword)).ShouldBeNull();
        }

        (await LoginAsync(email, GoodPassword)).ShouldBeNull();   // đúng mật khẩu nhưng đang khóa
        (await WithDbAsync(db => db.AdminSessions.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Four_wrong_passwords_do_not_lock()
    {
        var email = await CreateUserAsync();

        for (var i = 0; i < 4; i++)
        {
            await LoginAsync(email, BadPassword);
        }

        (await LoginAsync(email, GoodPassword)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Lock_writes_exactly_one_lockout_event_and_marks_later_attempts_as_locked()
    {
        var email = await CreateUserAsync();

        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(email, BadPassword);
        }

        await LoginAsync(email, GoodPassword);
        await LoginAsync(email, BadPassword);

        var logs = (await SecurityLogsAsync()).Where(l => l.Action.StartsWith("AdminLogin") || l.Action.StartsWith("AdminAccount")).ToList();
        logs.Count(l => l.Action == SecurityEventActions.AccountLockedOut).ShouldBe(1);
        logs.Where(l => l.Action == SecurityEventActions.LoginFailed).Select(Reason).ShouldBe(new[]
        {
            LoginFailureReasons.InvalidPassword, LoginFailureReasons.InvalidPassword, LoginFailureReasons.InvalidPassword,
            LoginFailureReasons.InvalidPassword, LoginFailureReasons.InvalidPassword,
            LoginFailureReasons.Locked, LoginFailureReasons.Locked,
        });
    }

    [Fact]
    public async Task Lock_lasts_until_15_minutes_after_the_fifth_failure()
    {
        var email = await CreateUserAsync();
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(email, BadPassword);
        }

        Clock.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(55));
        (await LoginAsync(email, GoodPassword)).ShouldBeNull();   // vẫn khóa

        Clock.Advance(TimeSpan.FromSeconds(10));                  // qua 15:00
        (await LoginAsync(email, GoodPassword)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Successful_login_resets_the_counter()
    {
        var email = await CreateUserAsync();

        for (var i = 0; i < 4; i++)
        {
            await LoginAsync(email, BadPassword);
        }

        (await LoginAsync(email, GoodPassword)).ShouldNotBeNull();

        for (var i = 0; i < 4; i++)
        {
            await LoginAsync(email, BadPassword);
        }

        (await LoginAsync(email, GoodPassword)).ShouldNotBeNull();   // 8 lần sai nhưng không có 5 lần liền nhau
    }

    [Fact]
    public async Task Locking_one_email_does_not_lock_another()
    {
        var locked = await CreateUserAsync();
        var other = await CreateUserAsync();

        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(locked, BadPassword);
        }

        (await LoginAsync(other, GoodPassword)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Failures_against_an_unknown_email_also_lock_that_email()
    {
        // Chống dò tài khoản: email không tồn tại cũng bị đếm và khóa y như email thật.
        const string ghost = "ghost@example.com";
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(ghost, BadPassword);
        }

        var attempts = await WithDbAsync(db => db.LoginAttempts.AsNoTracking().Where(a => a.NormalizedEmail == "GHOST@EXAMPLE.COM").ToListAsync());
        LoginAttemptPolicy.Evaluate(attempts.Select(a => a.ToEntry()), Clock.Now).IsLocked.ShouldBeTrue();

        var logs = await SecurityLogsAsync();
        logs.Count(l => l.Action == SecurityEventActions.AccountLockedOut).ShouldBe(1);
    }

    [Fact]
    public async Task Parallel_guesses_cannot_get_past_the_lock()
    {
        var email = await CreateUserAsync();

        // 20 lần sai đồng thời. Không tuần tự hóa theo email thì cả 20 cùng đọc "chưa khóa".
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => LoginAsync(email, BadPassword)));

        var logs = await SecurityLogsAsync();
        var failures = logs.Where(l => l.Action == SecurityEventActions.LoginFailed).ToList();
        failures.Count.ShouldBe(20);
        failures.Count(l => Reason(l) == LoginFailureReasons.InvalidPassword).ShouldBe(5);
        failures.Count(l => Reason(l) == LoginFailureReasons.Locked).ShouldBe(15);
        logs.Count(l => l.Action == SecurityEventActions.AccountLockedOut).ShouldBe(1);
    }

    // ---------- không rò thông tin nhạy cảm ----------

    [Fact]
    public async Task Password_and_session_id_never_appear_in_security_logs_or_attempt_rows()
    {
        var typedPassword = "Typed-Passw0rd!Leak?" + Guid.NewGuid().ToString("N")[..6];

        await LoginAsync(CoreServiceTestConsts.SuperAdminEmail, typedPassword);
        await LoginAsync("nobody@example.com", typedPassword);
        var ok = await LoginAsync(CoreServiceTestConsts.SuperAdminEmail, CoreServiceTestConsts.SuperAdminPassword);

        var dumps = new List<string>();
        // Tên bảng là hằng số trong test, đã tự quote đúng kiểu (ABP PascalCase cần dấu nháy kép, bảng dự án snake_case thì không).
        foreach (var table in new[] { "\"AbpSecurityLogs\"", "idn_login_attempts", "idn_admin_sessions", "\"AbpAuditLogs\"", "\"AbpAuditLogActions\"" })
        {
            dumps.AddRange(await WithDbAsync(db => db.Database
                .SqlQueryRaw<string>($"select t::text as \"Value\" from {table} t")
                .ToListAsync()));
        }

        dumps.Count.ShouldBeGreaterThan(0);
        dumps.ShouldAllBe(row => !row.Contains(typedPassword));
        dumps.ShouldAllBe(row => !row.Contains(CoreServiceTestConsts.SuperAdminPassword));
        dumps.ShouldAllBe(row => !row.Contains(ok!.SessionId));
    }

    [Fact]
    public async Task Text_that_is_not_an_email_is_not_copied_into_the_security_log()
    {
        // Người dùng gõ nhầm mật khẩu vào ô email.
        const string notAnEmail = "My-Secret-Passw0rd!";

        await LoginAsync(notAnEmail, BadPassword);

        var logs = await SecurityLogsAsync();
        logs.ShouldAllBe(l => l.UserName != notAnEmail);
        logs.Single(l => l.Action == SecurityEventActions.LoginFailed).UserName.ShouldBe("(invalid-email)");
    }

    [Fact]
    public void Auditing_is_disabled_on_the_login_service_controller_and_password_property()
    {
        typeof(SessionAppService).GetCustomAttributes(typeof(DisableAuditingAttribute), inherit: false).ShouldNotBeEmpty();
        typeof(SessionController).GetCustomAttributes(typeof(DisableAuditingAttribute), inherit: false).ShouldNotBeEmpty();
        typeof(LoginInput).GetProperty(nameof(LoginInput.Password))!
            .GetCustomAttributes(typeof(DisableAuditingAttribute), inherit: false).ShouldNotBeEmpty();
    }
}
