using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Identity.Sessions;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace CarDealership.CoreService.EntityFrameworkCore.Identity;

/// <summary>
/// Ngày 30 (ADR-005, Req 14.7, 21): bảng idn_admin_sessions trên PostgreSQL thật đúng cột, đúng index,
/// và KHÔNG chứa session id gốc ở bất kỳ ô nào.
/// </summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class AdminSessionTableTests : CoreServiceEntityFrameworkCoreTestBase
{
    private static readonly DateTime Now = new(2026, 10, 31, 4, 0, 0, DateTimeKind.Utc);

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
            .SqlQuery<string>($"select column_name || ':' || data_type || ':' || is_nullable as \"Value\" from information_schema.columns where table_name = 'idn_admin_sessions'")
            .ToListAsync());

        columns.ShouldBe(new[]
        {
            "id:uuid:NO",
            "token_hash:bytea:NO",
            "user_id:uuid:NO",
            "created_at:timestamp with time zone:NO",
            "last_seen_at:timestamp with time zone:NO",
            "revoked_at:timestamp with time zone:YES",
        }, ignoreOrder: true);
    }

    [Fact]
    public async Task Indexes_match_the_design_unique_hash_and_partial_user_index()
    {
        var indexes = await WithDbAsync(db => db.Database
            .SqlQuery<string>($"select indexdef as \"Value\" from pg_indexes where tablename = 'idn_admin_sessions'")
            .ToListAsync());

        indexes.ShouldContain(i => i.Contains("CREATE UNIQUE INDEX ux_idn_admin_sessions_token_hash") && i.Contains("(token_hash)"));
        indexes.ShouldContain(i => i.Contains("ix_idn_admin_sessions_user_active") && i.Contains("(user_id)") && i.Contains("WHERE (revoked_at IS NULL)"));
    }

    [Fact]
    public async Task Database_row_contains_the_hash_and_never_the_raw_session_id()
    {
        var token = SessionToken.Generate();
        var hash = SessionToken.Hash(token);
        var userId = Guid.NewGuid();

        await WithDbAsync(async db =>
        {
            db.AdminSessions.Add(new AdminSession(Guid.NewGuid(), hash, userId, Now));
            await db.SaveChangesAsync();
            return 0;
        });

        // Toàn bộ bảng dưới dạng text: giá trị gốc (và dạng base64/hex của nó) không được xuất hiện ở đâu cả.
        var dump = await WithDbAsync(db => db.Database
            .SqlQuery<string>($"select t::text as \"Value\" from idn_admin_sessions t")
            .ToListAsync());

        dump.Count.ShouldBe(1);
        dump[0].ShouldNotContain(token);
        dump[0].ShouldNotContain(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(token)));
        dump[0].ShouldNotContain(Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(token)).ToLowerInvariant());
        dump[0].ShouldContain(Convert.ToHexString(hash).ToLowerInvariant());   // hash thì có

        // Tra lại bằng hash được; tra bằng chuỗi gốc không ra gì.
        var found = await WithDbAsync(db => db.AdminSessions.Where(s => s.TokenHash == hash).ToListAsync());
        found.Single().UserId.ShouldBe(userId);
        var raw = System.Text.Encoding.UTF8.GetBytes(token);
        (await WithDbAsync(db => db.AdminSessions.Where(s => s.TokenHash == raw).CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Round_trip_keeps_utc_times_and_null_revoked_at()
    {
        var hash = SessionToken.Hash(SessionToken.Generate());
        var id = Guid.NewGuid();

        await WithDbAsync(async db =>
        {
            db.AdminSessions.Add(new AdminSession(id, hash, Guid.NewGuid(), Now));
            await db.SaveChangesAsync();
            return 0;
        });

        var loaded = await WithDbAsync(db => db.AdminSessions.AsNoTracking().SingleAsync(s => s.Id == id));
        loaded.CreatedAt.ShouldBe(Now);
        loaded.LastSeenAt.ShouldBe(Now);
        loaded.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
        loaded.RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Two_sessions_with_the_same_hash_are_rejected_by_the_unique_index()
    {
        var hash = SessionToken.Hash(SessionToken.Generate());

        await WithDbAsync(async db =>
        {
            db.AdminSessions.Add(new AdminSession(Guid.NewGuid(), hash, Guid.NewGuid(), Now));
            await db.SaveChangesAsync();
            return 0;
        });

        await Should.ThrowAsync<DbUpdateException>(() => WithDbAsync(async db =>
        {
            db.AdminSessions.Add(new AdminSession(Guid.NewGuid(), hash, Guid.NewGuid(), Now));
            await db.SaveChangesAsync();
            return 0;
        }));
    }

    [Fact]
    public async Task Database_rejects_a_token_hash_that_is_not_32_bytes()
    {
        // Bỏ qua entity (đã chặn ở constructor) để chứng minh CHECK constraint ở tầng DB.
        var ex = await Should.ThrowAsync<Exception>(() => WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "insert into idn_admin_sessions (id, token_hash, user_id, created_at, last_seen_at) " +
            "values (gen_random_uuid(), '\\x0102'::bytea, gen_random_uuid(), now(), now())")));

        ex.ToString().ShouldContain("ck_idn_admin_sessions_token_hash_len");
    }

    [Fact]
    public async Task Same_user_may_hold_many_sessions()
    {
        var userId = Guid.NewGuid();

        await WithDbAsync(async db =>
        {
            for (var i = 0; i < 3; i++)
            {
                db.AdminSessions.Add(new AdminSession(Guid.NewGuid(), SessionToken.Hash(SessionToken.Generate()), userId, Now));
            }

            await db.SaveChangesAsync();
            return 0;
        });

        (await WithDbAsync(db => db.AdminSessions.CountAsync(s => s.UserId == userId))).ShouldBe(3);
    }
}
