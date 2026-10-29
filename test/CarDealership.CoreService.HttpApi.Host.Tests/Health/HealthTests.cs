using System.Net;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Health.Tests;

/// <summary>
/// Ngày 14 (Req 22, 28.12): hợp đồng health endpoint.
///  - /health/live: 200, body rỗng, KHÔNG phụ thuộc DB — kiểm được KỂ CẢ khi không có Postgres,
///    nhờ đã đặt UseHealthChecks("/health/live") TRƯỚC UseAbpRequestLocalization().
///  - /health/ready: cần Postgres thật mới cho kết quả đúng nghĩa; vì localization middleware
///    đọc Setting từ DB chạy trước, DB chết sẽ thành 500 chứ không 503. Nên happy-path 200
///    chỉ chạy khi có DB; case 503 "DB chết sau khi app chạy" để Ngày 25 (Testcontainers).
/// </summary>
public class HealthTests : IClassFixture<CoreServiceWebApplicationFactory>
{
    private readonly CoreServiceWebApplicationFactory _factory;

    public HealthTests(CoreServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Live_returns_200_with_empty_body_without_db()
    {
        var client = _factory.CreateClient();

        var res = await client.GetAsync("/health/live");

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.ShouldBeNullOrEmpty();
    }

    [Fact]
    public async Task Ready_returns_200_when_db_available()
    {
        // xUnit 2.x không có Assert.SkipUnless; không có DB thì bỏ qua (no-op).
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        var client = _factory.CreateClient();

        var res = await client.GetAsync("/health/ready");

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
