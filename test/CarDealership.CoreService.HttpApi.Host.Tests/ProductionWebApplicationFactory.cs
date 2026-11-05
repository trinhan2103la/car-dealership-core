using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CarDealership.CoreService;

/// <summary>
/// Dựng Host với Environment = Production (giống container compose mặc định) để kiểm các hành vi
/// KHÔNG được phơi ở môi trường thật, ví dụ Swagger. Dùng connection string trong appsettings
/// (localhost:5432) nên test dùng nó phải qua DbProbe và bỏ qua khi không có Postgres.
/// Client mặc định kèm X-Edge-Key hợp lệ (xem <see cref="EdgeKeyTesting"/>).
/// </summary>
public class ProductionWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        EdgeKeyTesting.UseKey(builder, EdgeKeyTesting.Key);
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        EdgeKeyTesting.AddDefaultHeader(client);
    }
}