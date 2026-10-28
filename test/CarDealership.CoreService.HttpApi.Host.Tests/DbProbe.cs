using System;
using System.Net.Sockets;

namespace CarDealership.CoreService;

/// <summary>
/// Kiểm nhanh có PostgreSQL local lắng nghe ở localhost:5432 không (dùng để skip
/// các test cần DB thật khi chưa chạy docker compose). Không mở kết nối Npgsql,
/// chỉ thử TCP connect ngắn để tránh làm chậm test suite.
/// </summary>
public static class DbProbe
{
    public static bool IsPostgresAvailable(string host = "localhost", int port = 5432, int timeoutMs = 300)
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
