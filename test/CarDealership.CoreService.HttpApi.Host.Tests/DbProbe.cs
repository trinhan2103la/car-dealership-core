using System;
using System.Net.Sockets;
using Npgsql;

namespace CarDealership.CoreService;

/// <summary>
/// Quyết định các test cần PostgreSQL thật có chạy hay không.
/// - Cổng 5432 đóng: không có DB, trả false và test tự bỏ qua (máy chưa chạy compose).
/// - Cổng mở nhưng DB CHƯA migrate: NÉM LỖI có thông điệp rõ. Không được coi là "DB sẵn sàng":
///   AbpRequestLocalizationMiddleware đọc bảng AbpSettings trước mọi request, thiếu bảng là 500.
///   (Lỗi này làm CI đỏ lần đầu ở Ngày 22: service container Postgres rỗng, chưa chạy DbMigrator.)
/// - Cổng mở và đã migrate: trả true, test chạy thật.
/// </summary>
public static class DbProbe
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=cdp;User ID=cdp;Password=cdp_dev_pw;Timeout=5;";

    public static bool IsMigratedDatabaseAvailable()
    {
        if (!IsPortOpen("localhost", 5432))
        {
            return false;
        }

        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? DefaultConnectionString;

        using var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        using var command = new NpgsqlCommand("select to_regclass('public.\"AbpSettings\"') is not null", connection);
        var migrated = (bool)command.ExecuteScalar()!;

        if (!migrated)
        {
            throw new InvalidOperationException(
                "PostgreSQL dang chay nhung chua migrate (thieu bang AbpSettings). " +
                "Chay DbMigrator truoc khi test: " +
                "docker compose -f deploy/docker-compose.yml --env-file deploy/.env run --rm migrator " +
                "(hoac dotnet run --project src/CarDealership.CoreService.DbMigrator).");
        }

        return true;
    }

    private static bool IsPortOpen(string host, int port, int timeoutMs = 300)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.BeginConnect(host, port, null, null);
            var ok = connect.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeoutMs));
            if (ok)
            {
                client.EndConnect(connect);
            }
            return ok && client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
