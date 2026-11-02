using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Routing;

/// <summary>
/// Ngày 33: kiểm HTTP thật của POST /api/app/auth/sessions (route, binding, validation, body 401).
/// Cần PostgreSQL local đã migrate (DbProbe); không có thì bỏ qua như các test khác của project này.
/// Chỉ dùng email NGẪU NHIÊN không tồn tại nên luôn thất bại và không phụ thuộc mật khẩu SuperAdmin của máy.
/// Phần chứng minh khóa 5 lần, session, security event nằm ở EntityFrameworkCore.Tests/Identity/LoginTests.
/// </summary>
public class LoginEndpointTests : IClassFixture<CoreServiceWebApplicationFactory>
{
    private const string Url = "/api/app/auth/sessions";
    private const string CurrentUrl = "/api/app/auth/sessions/current";
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
    [InlineData("GET", null)]
    [InlineData("GET", "rac")]
    [InlineData("GET", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("DELETE", null)]
    [InlineData("DELETE", "rac")]
    [InlineData("DELETE", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Current_session_without_a_usable_session_id_gets_401_with_one_generic_body(string method, string? header)
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        var request = new HttpRequestMessage(new HttpMethod(method), CurrentUrl);
        if (header is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Admin-Session", header);
        }

        var res = await _factory.CreateClient().SendAsync(request);

        res.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await res.Content.ReadAsStringAsync()).ShouldContain("unauthenticated");
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
