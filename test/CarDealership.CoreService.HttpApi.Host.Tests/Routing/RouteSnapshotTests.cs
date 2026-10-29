using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Routing;

/// <summary>
/// Ngày 10 (Req 29.5, 28.12): khóa danh sách route khai báo tường minh.
/// - So danh sách route hiện tại với RouteSnapshot.verified.txt.
/// - Khi thêm route mới ở ngày sau, test sẽ ĐỎ cho tới khi cập nhật snapshot có chủ đích.
/// Lưu ý: /health/live và /health/ready KHÔNG xuất hiện ở đây vì được đăng ký bằng
/// app.UseHealthChecks(...) (middleware terminal), không phải endpoint routing.
/// Ngày 22: đã xóa HomeController (chỉ redirect "/" sang "/swagger"), nên hiện KHÔNG có route nào;
/// snapshot rỗng. Mọi controller ABP đã bị HideAbpModuleControllersConvention gỡ.
/// Dòng bắt đầu bằng '#' trong RouteSnapshot.verified.txt là chú thích và bị bỏ qua.
/// </summary>
public class RouteSnapshotTests : IClassFixture<CoreServiceWebApplicationFactory>
{
    private readonly CoreServiceWebApplicationFactory _factory;

    public RouteSnapshotTests(CoreServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Declared_routes_match_snapshot()
    {
        // Khởi tạo server để DI/route được dựng (không gửi request -> không chạm DB).
        _ = _factory.Server;

        var sources = _factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>();

        var actual = sources
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => "/" + (e.RoutePattern.RawText ?? string.Empty).TrimStart('/'))
            .Where(p => !p.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var expected = File.ReadAllLines(SnapshotPath())
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        actual.ShouldBe(expected, customMessage:
            "Route thay doi. Neu CO Y them/bot route, cap nhat RouteSnapshot.verified.txt thanh:\n"
            + string.Join("\n", actual));
    }

    [Theory]
    [InlineData("/api/abp/application-configuration")]
    [InlineData("/api/identity/users")]
    [InlineData("/api/permission-management/permissions")]
    [InlineData("/api/setting-management/emailing")]
    public async Task Abp_module_endpoints_return_404(string path)
    {
        // Cần Postgres thật: AbpRequestLocalizationMiddleware đọc Setting từ DB trước khi
        // request tới routing. Không có DB -> 500 (không phản ánh đúng 404 ở production).
        // xUnit 2.x không có Assert.SkipUnless; không có DB thì bỏ qua (no-op) để test suite
        // vẫn xanh ở máy chưa chạy compose. Có DB -> kiểm 404 thật.
        if (!DbProbe.IsPostgresAvailable())
        {
            return;
        }

        var client = _factory.CreateClient();
        var res = await client.GetAsync(path);
        res.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static string SnapshotPath()
        => Path.Combine(AppContext.BaseDirectory, "RouteSnapshot.verified.txt");
}
