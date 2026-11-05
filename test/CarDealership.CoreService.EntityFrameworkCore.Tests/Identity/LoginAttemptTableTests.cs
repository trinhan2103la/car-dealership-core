using System;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Identity;
using CarDealership.CoreService.Identity.Login;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Xunit;
using AbpIdentityUser = Volo.Abp.Identity.IdentityUser;

namespace CarDealership.CoreService.EntityFrameworkCore.Identity;

/// <summary>
/// Bảng idn_login_attempts trên PostgreSQL thật, policy chạy trên dữ liệu đọc từ DB,
/// và lockout có sẵn của ASP.NET Identity đã tắt (một nguồn sự thật).
/// </summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class LoginAttemptTableTests : CoreServiceEntityFrameworkCoreTestBase
{
    private static readonly DateTime T0 = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

    private Task<T> WithDbAsync<T>(Func<CoreServiceDbContext, Task<T>> action) =>
        WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CoreServiceDbContext>>().GetDbContextAsync();
            return await action(db);
        });

    [Fact]
    public async Task Table_has_exactly_the_columns_of_the_design()
    {
        var columns = await WithDbAsync(db => db.Database
            .SqlQuery<string>($"select column_name || ':' || data_type || ':' || is_nullable as \"Value\" from information_schema.columns where table_name = 'idn_login_attempts'")
            .ToListAsync());

        columns.ShouldBe(new[]
        {
            "id:uuid:NO",
            "normalized_email:text:NO",
            "user_id:uuid:YES",
            "succeeded:boolean:NO",
            "attempted_at:timestamp with time zone:NO",
        }, ignoreOrder: true);
    }

    [Fact]
    public async Task Index_is_on_email_and_attempted_at_descending()
    {
        var indexes = await WithDbAsync(db => db.Database
            .SqlQuery<string>($"select indexdef as \"Value\" from pg_indexes where tablename = 'idn_login_attempts'")
            .ToListAsync());

        indexes.ShouldContain(i => i.Contains("ix_idn_login_attempts_email_attempted_at")
                                   && i.Contains("(normalized_email, attempted_at DESC)"));
    }

    [Fact]
    public async Task Unknown_email_is_stored_with_null_user_and_utc_times_round_trip()
    {
        var email = LoginEmail.Normalize("  Nobody@Example.com ");

        await WithDbAsync(async db =>
        {
            db.LoginAttempts.Add(new LoginAttempt(Guid.NewGuid(), email, null, false, T0));
            await db.SaveChangesAsync();
            return 0;
        });

        var loaded = await WithDbAsync(db => db.LoginAttempts.AsNoTracking().SingleAsync(a => a.NormalizedEmail == email));
        loaded.UserId.ShouldBeNull();
        loaded.Succeeded.ShouldBeFalse();
        loaded.AttemptedAt.ShouldBe(T0);
        loaded.AttemptedAt.Kind.ShouldBe(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Policy_replays_history_read_from_the_database_and_isolates_per_email()
    {
        var target = LoginEmail.Normalize("target@example.com");
        var other = LoginEmail.Normalize("other@example.com");

        await WithDbAsync(async db =>
        {
            for (var i = 0; i < 5; i++)
            {
                db.LoginAttempts.Add(new LoginAttempt(Guid.NewGuid(), target, null, false, T0.AddMinutes(i)));
            }

            db.LoginAttempts.Add(new LoginAttempt(Guid.NewGuid(), other, null, false, T0));
            await db.SaveChangesAsync();
            return 0;
        });

        async Task<LoginDecision> EvaluateAsync(string email, DateTime now)
        {
            var history = await WithDbAsync(db => db.LoginAttempts.AsNoTracking()
                .Where(a => a.NormalizedEmail == email)
                .OrderByDescending(a => a.AttemptedAt)
                .ToListAsync());
            return LoginAttemptPolicy.Evaluate(history.Select(a => a.ToEntry()), now);
        }

        (await EvaluateAsync(target, T0.AddMinutes(4))).IsLocked.ShouldBeTrue();
        (await EvaluateAsync(target, T0.AddMinutes(19))).IsLocked.ShouldBeFalse();
        (await EvaluateAsync(other, T0.AddMinutes(4))).IsLocked.ShouldBeFalse();
    }

    [Fact]
    public async Task Aspnet_identity_lockout_is_off_so_login_attempt_policy_is_the_only_lock()
    {
        var created = await WithUnitOfWorkAsync(async () =>
        {
            var userManager = GetRequiredService<IdentityUserManager>();
            var name = "u" + Guid.NewGuid().ToString("N")[..12];
            var user = new AbpIdentityUser(Guid.NewGuid(), name, name + "@example.com");
            (await userManager.CreateAsync(user, "Aa1!" + new string('x', 16))).Succeeded.ShouldBeTrue();
            return await userManager.FindByNameAsync(name);
        });

        created!.LockoutEnabled.ShouldBeFalse();

        // Sai nhiều lần qua UserManager (đường có đếm của ASP.NET Identity) cũng không tạo lockout.
        await WithUnitOfWorkAsync(async () =>
        {
            var userManager = GetRequiredService<IdentityUserManager>();
            var user = (await userManager.FindByIdAsync(created.Id.ToString()))!;
            for (var i = 0; i < 10; i++)
            {
                await userManager.AccessFailedAsync(user);
            }

            (await userManager.IsLockedOutAsync(user)).ShouldBeFalse();
        });
    }
}
