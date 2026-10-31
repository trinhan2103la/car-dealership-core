using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace CarDealership.CoreService.EntityFrameworkCore;

/// <summary>
/// Ngày 25: một container PostgreSQL 18 cho CẢ collection test EF Core (không phải mỗi class một container).
/// xUnit tạo fixture này trước test đầu tiên của collection và hủy sau test cuối.
/// - InitializeAsync: khởi động container, áp migration thật (không dùng EnsureCreated).
/// - ResetDatabase: TRUNCATE mọi bảng trừ lịch sử migration; module test gọi trước mỗi test.
/// Cần Docker đang chạy (Docker Desktop ở máy dev; runner ubuntu-latest của GitHub có sẵn).
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:18";

    private static string? _connectionString;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();

    /// <summary>Chuỗi kết nối tới container; chỉ có sau khi fixture khởi động.</summary>
    public static string ConnectionString => _connectionString
        ?? throw new InvalidOperationException(
            "PostgresFixture chua khoi dong. Class test EF Core phai co " +
            "[Collection(CoreServiceTestConsts.CollectionDefinitionName)].");

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();

        // Giống CoreServiceEntityFrameworkCoreModule; đặt sớm vì migration chạy trước khi ABP khởi động.
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var options = new DbContextOptionsBuilder<CoreServiceDbContext>()
            .UseNpgsql(_connectionString)
            .Options;

        await using var dbContext = new CoreServiceDbContext(options);
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _connectionString = null;
        await _container.DisposeAsync();
    }

    public static void ResetDatabase()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            DO $$
            DECLARE tables text;
            BEGIN
                SELECT string_agg(format('%I.%I', schemaname, tablename), ', ')
                  INTO tables
                  FROM pg_tables
                 WHERE schemaname = 'public'
                   AND tablename <> '__EFMigrationsHistory';

                IF tables IS NOT NULL THEN
                    EXECUTE 'TRUNCATE TABLE ' || tables || ' RESTART IDENTITY CASCADE';
                END IF;
            END $$;
            """;
        command.ExecuteNonQuery();
    }
}
