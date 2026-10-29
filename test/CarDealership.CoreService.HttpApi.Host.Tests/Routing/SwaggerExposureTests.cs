using System.Net;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Routing;

/// <summary>
/// Ngày 22 (Req 29.5, design "Swagger UI chỉ bật ở dev"): Swagger chỉ phục vụ ở Development.
/// Phát hiện ở nghiệm thu P0 (Ngày 21): container Production vẫn trả 200 cho /swagger.
/// Cả hai test cần Postgres thật vì AbpRequestLocalizationMiddleware đọc Setting từ DB trước
/// khi request tới Swagger; không có DB thì bỏ qua (xUnit 2.x không có Assert.SkipUnless).
/// </summary>
public class SwaggerExposureTests :
    IClassFixture<ProductionWebApplicationFactory>,
    IClassFixture<CoreServiceWebApplicationFactory>
{
    private readonly ProductionWebApplicationFactory _production;
    private readonly CoreServiceWebApplicationFactory _development;

    public SwaggerExposureTests(
        ProductionWebApplicationFactory production,
        CoreServiceWebApplicationFactory development)
    {
        _production = production;
        _development = development;
    }

    [Theory]
    [InlineData("/swagger")]
    [InlineData("/swagger/index.html")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task Swagger_is_not_served_in_production(string path)
    {
        if (!DbProbe.IsPostgresAvailable())
        {
            return;
        }

        var client = _production.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var res = await client.GetAsync(path);

        res.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Swagger_json_is_served_in_development()
    {
        if (!DbProbe.IsPostgresAvailable())
        {
            return;
        }

        var client = _development.CreateClient();

        var res = await client.GetAsync("/swagger/v1/swagger.json");

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
