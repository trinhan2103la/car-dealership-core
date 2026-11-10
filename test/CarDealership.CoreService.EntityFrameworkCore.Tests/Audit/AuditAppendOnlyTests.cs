using System;
using System.Threading.Tasks;
using CarDealership.CoreService.Audit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace CarDealership.CoreService.EntityFrameworkCore.Audit;

/// <summary>
/// aud_entries append-only THẬT ở tầng DB: role ứng dụng chỉ INSERT + SELECT, UPDATE/DELETE/TRUNCATE bị
/// PostgreSQL từ chối (SQLSTATE 42501). Kết nối bằng role ứng dụng, KHÔNG phải role chủ bảng.
/// </summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class AuditAppendOnlyTests : CoreServiceEntityFrameworkCoreTestBase
{
    private const string InsufficientPrivilege = "42501";
    private static readonly DateTime T0 = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);

    private static async Task<NpgsqlConnection> OpenAppConnectionAsync()
    {
        var connection = new NpgsqlConnection(PostgresFixture.AppConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, Guid? id = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (id.HasValue)
        {
            command.Parameters.AddWithValue("id", id.Value);
        }

        return await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertAsync(NpgsqlConnection connection, Guid id)
    {
        (await ExecuteAsync(connection, """
            INSERT INTO aud_entries (id, occurred_at, actor_id, action, entity_type, entity_id, correlation_id, changes)
            VALUES (@id, now(), NULL, 'Publish', 'CarModel', 'x', 'cid-1', '[]'::jsonb)
            """, id)).ShouldBe(1);
    }

    [Fact]
    public async Task App_role_can_insert_and_select()
    {
        var id = Guid.NewGuid();
        await using var connection = await OpenAppConnectionAsync();

        await InsertAsync(connection, id);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM aud_entries WHERE id = @id";
        command.Parameters.AddWithValue("id", id);
        ((long)(await command.ExecuteScalarAsync())!).ShouldBe(1);
    }

    [Theory]
    [InlineData("UPDATE aud_entries SET action = 'Hacked'")]
    [InlineData("DELETE FROM aud_entries")]
    [InlineData("TRUNCATE TABLE aud_entries")]
    public async Task App_role_cannot_update_delete_or_truncate(string sql)
    {
        await using var connection = await OpenAppConnectionAsync();
        await InsertAsync(connection, Guid.NewGuid());

        var ex = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(connection, sql));
        ex.SqlState.ShouldBe(InsufficientPrivilege);
    }

    [Fact]
    public async Task Rows_written_by_app_role_are_untouched_after_rejected_attempts()
    {
        var id = Guid.NewGuid();
        await using var connection = await OpenAppConnectionAsync();
        await InsertAsync(connection, id);

        await Should.ThrowAsync<PostgresException>(() =>
            ExecuteAsync(connection, "UPDATE aud_entries SET action = 'Hacked' WHERE id = @id", id));

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT action FROM aud_entries WHERE id = @id";
        command.Parameters.AddWithValue("id", id);
        ((string)(await command.ExecuteScalarAsync())!).ShouldBe("Publish");
    }

    [Fact]
    public async Task App_role_privileges_on_the_table_are_exactly_select_and_insert()
    {
        await using var connection = await OpenAppConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT string_agg(privilege_type, ',' ORDER BY privilege_type)
              FROM information_schema.role_table_grants
             WHERE table_name = 'aud_entries' AND grantee = current_user
            """;

        ((string)(await command.ExecuteScalarAsync())!).ShouldBe("INSERT,SELECT");
    }

    [Fact]
    public async Task Table_has_exactly_the_columns_of_the_design()
    {
        var columns = await WithDbAsync(db => db.Database
            .SqlQuery<string>($"select column_name || ':' || data_type || ':' || is_nullable as \"Value\" from information_schema.columns where table_name = 'aud_entries'")
            .ToListAsync());

        columns.ShouldBe(new[]
        {
            "id:uuid:NO",
            "occurred_at:timestamp with time zone:NO",
            "actor_id:uuid:YES",
            "action:text:NO",
            "entity_type:text:NO",
            "entity_id:text:NO",
            "correlation_id:text:NO",
            "changes:jsonb:NO",
        }, ignoreOrder: true);
    }

    [Fact]
    public async Task The_three_indexes_of_the_design_exist()
    {
        var indexes = await WithDbAsync(db => db.Database
            .SqlQuery<string>($"select indexdef as \"Value\" from pg_indexes where tablename = 'aud_entries'")
            .ToListAsync());

        indexes.ShouldContain(i => i.Contains("ix_aud_entries_occurred_at") && i.Contains("(occurred_at DESC)"));
        indexes.ShouldContain(i => i.Contains("ix_aud_entries_entity") && i.Contains("(entity_type, entity_id)"));
        indexes.ShouldContain(i => i.Contains("ix_aud_entries_actor_occurred_at") && i.Contains("(actor_id, occurred_at DESC)"));
    }

    [Fact]
    public async Task Entity_round_trips_with_utc_time_and_jsonb_changes()
    {
        var id = Guid.NewGuid();
        var actor = Guid.NewGuid();
        const string changes = """[{"field":"Name","before":"A","after":"B"}]""";

        await WithDbAsync(async db =>
        {
            db.AuditEntries.Add(new AuditEntry(id, T0, actor, "Update", "Brand", "b1", "cid-2", changes));
            await db.SaveChangesAsync();
            return 0;
        });

        var loaded = await WithDbAsync(db => db.AuditEntries.AsNoTracking().SingleAsync(e => e.Id == id));
        loaded.OccurredAt.ShouldBe(T0);
        loaded.OccurredAt.Kind.ShouldBe(DateTimeKind.Utc);
        loaded.ActorId.ShouldBe(actor);
        loaded.CorrelationId.ShouldBe("cid-2");
        loaded.Changes.ShouldContain("\"field\"");
    }

    private Task<T> WithDbAsync<T>(Func<CoreServiceDbContext, Task<T>> action) =>
        WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CoreServiceDbContext>>().GetDbContextAsync();
            return await action(db);
        });
}
