using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CarDealership.CoreService;

/// <summary>
/// Dựng CoreService Host trong bộ nhớ để kiểm route và health.
/// Dùng connection string THẬT trong appsettings (localhost:5432) hoặc biến ConnectionStrings__Default. Vì vậy:
///  - /health/live luôn 200 (đã đặt trước localization, không chạm DB).
///  - /health/ready và các endpoint cần DB chỉ đúng khi có Postgres local đã migrate;
///    các test đó tự bỏ qua qua DbProbe khi không có DB (trên CI: REQUIRE_DB_TESTS=true thì thành lỗi).
/// Client mặc định đã kèm X-Edge-Key hợp lệ; test muốn thử "không có khóa" thì xóa header khỏi client.
/// </summary>
public class CoreServiceWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        EdgeKeyTesting.UseKey(builder, EdgeKeyTesting.Key);
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        EdgeKeyTesting.AddDefaultHeader(client);
    }
}