using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CarDealership.CoreService;

/// <summary>
/// Dựng CoreService Host trong bộ nhớ để kiểm route và health (Ngày 10, 14).
/// Dùng connection string THẬT trong appsettings (localhost:5432). Vì vậy:
///  - /health/live luôn 200 (đã đặt trước localization, không chạm DB).
///  - /health/ready và các endpoint cần DB chỉ đúng khi có Postgres local đang chạy;
///    các test đó tự skip qua DbProbe khi không có DB.
/// Testcontainers (Ngày 25) sẽ thay bằng Postgres do test tự quản lý vòng đời.
/// </summary>
public class CoreServiceWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }
}
