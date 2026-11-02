using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Middleware.Tests;

/// <summary>
/// Ngày 36 (Req 28.12, 16.12): Edge key trên HOST THẬT (toàn bộ pipeline ABP). Các ca "bị chặn" không cần DB vì Edge key
/// đứng trước localization/routing; ca có khóa thì chỉ kiểm những endpoint không đụng DB.
/// </summary>
public class EdgeKeyHostTests : IClassFixture<CoreServiceWebApplicationFactory>
{
    private readonly CoreServiceWebApplicationFactory _factory;

    public EdgeKeyHostTests(CoreServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientWithoutKey()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Remove(EdgeKeyTesting.Header);
        return client;
    }

    [Fact]
    public async Task Health_live_needs_no_key_and_returns_200_with_empty_body()
    {
        var res = await ClientWithoutKey().GetAsync("/health/live");

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("GET", "/health/ready")]
    [InlineData("GET", "/api/abp/application-configuration")]
    [InlineData("GET", "/swagger/v1/swagger.json")]
    [InlineData("GET", "/swagger/index.html")]
    [InlineData("POST", "/api/app/auth/sessions")]
    [InlineData("GET", "/api/app/auth/sessions/current")]
    [InlineData("DELETE", "/api/app/auth/sessions/current")]
    [InlineData("GET", "/does-not-exist")]
    [InlineData("GET", "/")]
    public async Task Without_the_key_every_other_endpoint_is_a_silent_404_even_ones_that_exist(string method, string path)
    {
        var res = await ClientWithoutKey().SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        res.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await res.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_wrong_key_is_treated_exactly_like_no_key()
    {
        var client = ClientWithoutKey();
        client.DefaultRequestHeaders.TryAddWithoutValidation(EdgeKeyTesting.Header, EdgeKeyTesting.Key + "x");

        var ready = await client.GetAsync("/health/ready");
        var login = await client.PostAsync("/api/app/auth/sessions",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        ready.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        login.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Rejection_responses_look_the_same_for_an_existing_and_a_nonexistent_route()
    {
        var client = ClientWithoutKey();

        var existing = await client.PostAsync("/api/app/auth/sessions", new StringContent("{}"));
        var missing = await client.PostAsync("/api/not/a/route", new StringContent("{}"));

        existing.StatusCode.ShouldBe(missing.StatusCode);
        (await existing.Content.ReadAsStringAsync()).ShouldBe(await missing.Content.ReadAsStringAsync());
        existing.Content.Headers.ContentLength.ShouldBe(missing.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task With_the_key_the_request_reaches_the_application()
    {
        // /health/live không phân biệt được; dùng route có thật nhưng sai method: tới được routing thì không còn là 404 "im lặng".
        // Cần DB vì localization đọc Setting; không có DB thì bỏ qua như các test cùng loại.
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        var res = await _factory.CreateClient().GetAsync("/api/app/auth/sessions");

        res.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }
}

/// <summary>Ngày 36 (Req 16.12): thiếu EDGE_KEY thì host KHÔNG khởi động được.</summary>
public class EdgeKeyStartupTests
{
    private sealed class FactoryWithKey(string? key) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            EdgeKeyTesting.UseKey(builder, key);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    public void Host_refuses_to_start_without_a_valid_edge_key(string? key)
    {
        using var factory = new FactoryWithKey(key);

        // Khởi động thất bại: Program.cs bắt exception, log Fatal rồi trả mã 1 -> factory không dựng được host.
        Should.Throw<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void Host_starts_with_a_valid_key()
    {
        using var factory = new FactoryWithKey(EdgeKeyTesting.Key);

        Should.NotThrow(() => factory.CreateClient());
    }
}
