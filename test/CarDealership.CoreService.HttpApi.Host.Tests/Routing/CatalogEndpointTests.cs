using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Routing;

/// <summary>
/// Catalog qua pipeline HTTP THẬT (Edge key → JWT → AdminPrincipalMiddleware → [Authorize(permission)] → ánh xạ mã lỗi).
/// Cần PostgreSQL đã migrate (DbProbe). Phần đăng nhập thật chỉ chạy khi có SEED_SUPERADMIN_EMAIL/PASSWORD của DB đó.
/// </summary>
public class CatalogEndpointTests : IClassFixture<CoreServiceWebApplicationFactory>
{
    private readonly CoreServiceWebApplicationFactory _factory;

    public CatalogEndpointTests(CoreServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientWithIdentity(Guid? sub, Guid? sid)
    {
        var payload = TestServiceJwt.ValidPayload(now: DateTime.UtcNow.AddSeconds(-2));
        if (sub is not null && sid is not null)
        {
            payload["sub"] = sub.ToString()!;
            payload["sid"] = sid.ToString()!;
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestServiceJwt.Create(payload));
        return client;
    }

    [Fact]
    public async Task Public_brands_need_no_admin_session()
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        var res = await ClientWithIdentity(null, null).GetAsync("/api/catalog/public/brands");

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_endpoint_without_admin_identity_is_forbidden()
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        var res = await ClientWithIdentity(null, null).PostAsJsonAsync("/api/catalog/brands", new { name = "X", slug = "x" });

        // Service JWT hợp lệ nhưng không có Admin_User: ABP từ chối theo Permission (không chạm dữ liệu).
        res.StatusCode.ShouldBeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_endpoint_with_an_unknown_session_is_401_before_reaching_the_controller()
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        var res = await ClientWithIdentity(Guid.NewGuid(), Guid.NewGuid()).GetAsync("/api/catalog/brands");

        res.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await res.Content.ReadAsStringAsync()).ShouldContain("unauthenticated");
    }

    [Fact]
    public async Task Invalid_public_query_is_400_and_unknown_slug_is_404()
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        var client = ClientWithIdentity(null, null);

        (await client.GetAsync("/api/catalog/public/car-models?pageSize=49&sort=random")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/catalog/public/car-models/by-slug?brandSlug=khong-co&modelSlug=khong-co"))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Super_admin_session_creates_a_brand_and_a_duplicate_is_409()
    {
        var email = Environment.GetEnvironmentVariable("SEED_SUPERADMIN_EMAIL");
        var password = Environment.GetEnvironmentVariable("SEED_SUPERADMIN_PASSWORD");
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password) || !DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        var login = await ClientWithIdentity(null, null).PostAsJsonAsync("/api/app/auth/sessions", new { email, password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var userId = body.RootElement.GetProperty("userId").GetGuid();
        var sessionId = body.RootElement.GetProperty("sessionId").GetGuid();
        var admin = ClientWithIdentity(userId, sessionId);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var create = await admin.PostAsJsonAsync("/api/catalog/brands", new { name = $"Brand {suffix}", slug = $"brand-{suffix}" });
        create.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await create.Content.ReadAsStringAsync()).ShouldContain("\"status\":\"Active\"");

        var duplicate = await admin.PostAsJsonAsync("/api/catalog/brands", new { name = $"BRAND {suffix}", slug = $"other-{suffix}" });
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var invalid = await admin.PostAsJsonAsync("/api/catalog/brands", new { name = "", slug = "-x-" });
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var model = await admin.PostAsJsonAsync("/api/catalog/car-models", new { brandId = Guid.NewGuid(), name = "X", slug = "x" });
        model.StatusCode.ShouldBe(HttpStatusCode.BadRequest);   // brandId không tồn tại: lỗi theo trường

        (await admin.GetAsync($"/api/catalog/car-models/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
