using System;
using System.Net.Sockets;
using Npgsql;

namespace CarDealership.CoreService;

/// <summary>
/// Quyết định các test cần PostgreSQL thật có chạy hay không.
/// - Cổng đóng: không có DB, trả false và test tự bỏ qua (máy chưa chạy compose).
/// - Cổng mở nhưng DB CHƯA migrate: NÉM LỖI có thông điệp rõ. Không được coi là "DB sẵn sàng":
///   AbpRequestLocalizationMiddleware đọc bảng AbpSettings trước mọi request, thiếu bảng là 500.
///   (Ví dụ: service container Postgres trên CI còn rỗng vì chưa chạy DbMigrator.)
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
        // Lấy ĐÚNG host/port của DB mà test sẽ nối tới từ ConnectionStrings__Default (trên CI là service container).
        // KHÔNG dò cổng 127.0.0.1:5432 cứng nữa: cổng đó va chạm với Testcontainers của project khác và phụ thuộc
        // thứ tự Testcontainers kịp mở hay chưa -> flaky. Dò đúng host/port mới phản ánh DB thật test dùng.
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? DefaultConnectionString;

        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var host = string.IsNullOrWhiteSpace(builder.Host) ? "localhost" : builder.Host;
        var port = builder.Port;

        // "localhost" thử ::1 trước và ăn hết timeout trước khi rơi về 127.0.0.1; ép IPv4 để tránh trễ ::1 như ghi chú gốc.
        var probeHost = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ? "127.0.0.1" : host;

        if (!IsPortOpen(probeHost, port))
        {
            // Trên CI, "không có DB" là LỖI chứ không được bỏ qua: nếu không, mọi test cần DB sẽ lặng lẽ xanh giả.
            if (string.Equals(Environment.GetEnvironmentVariable("REQUIRE_DB_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"REQUIRE_DB_TESTS=true nhung khong ket noi duoc PostgreSQL o {probeHost}:{port} " +
                    $"(host/port lay tu ConnectionStrings__Default). Test can DB khong duoc phep bi bo qua tren CI.");
            }

            return false;
        }

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

    // Dò cổng TCP bằng host/port đã resolve (IPv4 tường minh khi là localhost): "localhost" thử ::1 trước và ăn hết
    // timeout, nên khi DB chỉ mở trên 127.0.0.1 thì probe báo "cổng đóng" và mọi test cần DB lặng lẽ bị bỏ qua (xanh giả).
    // Thử lại vài nhịp: probe chạy GIỮA lúc 189 test song song khởi động (WebApplicationFactory + EF warmup) gây áp lực CPU,
    // một lần WaitOne ngắn có thể trượt vì thread chưa kịp được lập lịch -> báo "cổng đóng" GIẢ dù DB sẵn sàng.
    // Cổng đóng THẬT vẫn trả false sau khi hết số lần thử (giữ nguyên ý đồ (a)/(b)).
    private static bool IsPortOpen(string host, int port, int timeoutMs = 500, int attempts = 5)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            try
            {
                using var client = new TcpClient();
                var connect = client.BeginConnect(host, port, null, null);
                var ok = connect.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(timeoutMs));
                if (ok)
                {
                    client.EndConnect(connect);
                    if (client.Connected)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Nuốt lỗi và thử lại: cổng có thể chưa mở ở nhịp này.
            }

            if (attempt < attempts - 1)
            {
                System.Threading.Thread.Sleep(200);
            }
        }

        return false;
    }
}
