using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Routing;

/// <summary>
/// Kiểm HTTP thật của POST /api/app/auth/sessions (route, binding, validation, body 401).
/// Cần PostgreSQL local đã migrate (DbProbe); không có thì bỏ qua như các test khác của project này.
/// Chỉ dùng email NGẪU NHIÊN không tồn tại nên luôn thất bại và không phụ thuộc mật khẩu SuperAdmin của máy.
/// Phần chứng minh khóa 5 lần, session, security event nằm ở EntityFrameworkCore.Tests/Identity/LoginTests.
/// </summary>
public class LoginEndpointTests : IClassFixture<CoreServiceWebApplicationFactory>
{
    private const string Url = "/api/app/auth/sessions";
    private const string CurrentUrl = "/api/app/auth/sessions/current";
    private const string RefreshUrl = "/api/app/auth/sessions/refresh";
    private const string RevokeUrl = "/api/app/auth/sessions/revoke";
    private readonly CoreServiceWebApplicationFactory _factory;

    public LoginEndpointTests(CoreServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string RandomEmail() => $"nobody{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Unknown_email_gets_401_with_the_generic_body()
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        var res = await _factory.CreateClient().PostAsJsonAsync(Url, new { email = RandomEmail(), password = "Whatever-Passw0rd!2026" });

        res.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await res.Content.ReadAsStringAsync();
        body.ShouldContain("invalid_credentials");
        body.ShouldNotContain("sessionId", Case.Insensitive);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"email\":\"\",\"password\":\"x\"}")]
    [InlineData("{\"email\":\"a@b.co\",\"password\":\"\"}")]
    [InlineData("{\"email\":\"a@b.co\"}")]
    public async Task Missing_or_empty_fields_get_400_and_are_not_counted_as_attempts(string json)
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        var res = await _factory.CreateClient().PostAsync(Url,
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Overlong_email_or_password_get_400()
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        var client = _factory.CreateClient();

        (await client.PostAsJsonAsync(Url, new { email = new string('a', 321), password = "x" }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(Url, new { email = RandomEmail(), password = new string('p', 129) }))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("rac")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]     // đúng dạng nhưng không tồn tại
    public async Task Refresh_and_revoke_with_an_unusable_token_get_401_with_one_generic_body(string token)
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        var client = _factory.CreateClient();
        foreach (var url in new[] { RefreshUrl, RevokeUrl })
        {
            var res = await client.PostAsJsonAsync(url, new { refreshToken = token });

            res.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            var body = await res.Content.ReadAsStringAsync();
            body.ShouldContain("unauthenticated");
            body.ShouldNotContain(token);
        }
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"refreshToken\":\"\"}")]
    public async Task Refresh_without_a_token_gets_400(string json)
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        var res = await _factory.CreateClient().PostAsync(RefreshUrl,
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Current_session_without_admin_identity_claims_gets_401()
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        // Service JWT hợp lệ nhưng không có sub/sid: không có phiên nào để tra.
        var res = await _factory.CreateClient().GetAsync(CurrentUrl);

        res.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await res.Content.ReadAsStringAsync()).ShouldContain("unauthenticated");
    }

    [Fact]
    public async Task Current_session_with_a_well_formed_but_unknown_session_gets_401()
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        var payload = TestServiceJwt.ValidPayload(now: DateTime.UtcNow.AddSeconds(-2));
        payload["sub"] = Guid.NewGuid().ToString();
        payload["sid"] = Guid.NewGuid().ToString();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestServiceJwt.Create(payload));

        var res = await client.GetAsync(CurrentUrl);

        res.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await res.Content.ReadAsStringAsync()).ShouldContain("unauthenticated");
    }

    [Theory]
    [InlineData("sub", null)]                 // có sub mà không có sid
    [InlineData(null, "sid")]                 // có sid mà không có sub
    [InlineData("not-a-guid", "not-a-guid")]  // sai dạng
    public async Task Service_jwt_with_an_inconsistent_admin_identity_is_rejected_at_authentication(string? sub, string? sid)
    {
        var payload = TestServiceJwt.ValidPayload(now: DateTime.UtcNow.AddSeconds(-2));
        if (sub is not null)
        {
            payload["sub"] = sub == "sub" ? Guid.NewGuid().ToString() : sub;
        }

        if (sid is not null)
        {
            payload["sid"] = sid == "sid" ? Guid.NewGuid().ToString() : sid;
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestServiceJwt.Create(payload));

        // /health/ready cần JWT (fallback policy) nên chứng minh được 401 ở bước xác thực, trước cả khi chạm controller.
        var res = await client.GetAsync("/health/ready");

        res.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_or_put_on_the_current_session_route_is_405()
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        var client = _factory.CreateClient();

        (await client.PostAsJsonAsync(CurrentUrl, new { })).StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await client.PutAsJsonAsync(CurrentUrl, new { })).StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        (await client.DeleteAsync(CurrentUrl)).StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);   // Bỏ DELETE
        (await client.GetAsync(RefreshUrl)).StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Get_on_the_login_route_is_405()
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        (await _factory.CreateClient().GetAsync(Url)).StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }
}
