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
    static DbProbe()
    {
        // Npgsql đọc công tắc này MỘT LẦN khi khởi tạo kiểu tĩnh. Probe mở kết nối TRƯỚC khi host dựng (host mới đặt công tắc
        // ở PreConfigureServices), nên không đặt ở đây thì cả process test chạy ở chế độ timestamp mới và mọi INSERT
        // DateTime Kind=Utc vào cột "timestamp without time zone" của ABP (AbpAuditLogs) đều lỗi 500.
        // Production không bị: Program/host đặt công tắc trước khi chạm Npgsql.
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    }

    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=cdp;User ID=cdp;Password=cdp_dev_pw;Timeout=5;";

    public static bool IsMigratedDatabaseAvailable()
    {
        if (!IsPortOpen("127.0.0.1", 5432))
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

    // Dò bằng IPv4 tường minh: "localhost" thử ::1 trước và ăn hết 300 ms, nên khi compose chỉ mở cổng trên 127.0.0.1
    // thì probe báo "cổng đóng" và MỌI test cần DB lặng lẽ bị bỏ qua (xanh giả). Đã gặp ở Ngày 33.
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
