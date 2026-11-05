using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Auth.Tests;

/// <summary>
/// Service JWT trên HOST THẬT. Các ca bị Edge key chặn hoặc /health/live không cần DB; các ca
/// tới tầng xác thực/ủy quyền cần DB (AbpRequestLocalizationMiddleware đọc Setting trước), nên dùng DbProbe như các test cùng loại.
/// </summary>
public class ServiceCredentialHostTests : IClassFixture<CoreServiceWebApplicationFactory>
{
    private readonly CoreServiceWebApplicationFactory _factory;

    public ServiceCredentialHostTests(CoreServiceWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientWithScope(string? scope)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = scope is null
            ? null
            : new AuthenticationHeaderValue("Bearer", TestServiceJwt.Create(scope));
        return client;
    }

    [Fact]
    public async Task Health_live_needs_neither_edge_key_nor_jwt()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Remove(EdgeKeyTesting.Header);
        client.DefaultRequestHeaders.Authorization = null;

        var res = await client.GetAsync("/health/live");

        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Edge_key_is_checked_before_the_jwt_a_valid_jwt_without_edge_key_is_a_silent_404()
    {
        var client = ClientWithScope("bff");
        client.DefaultRequestHeaders.Remove(EdgeKeyTesting.Header);

        var res = await client.GetAsync("/health/ready");

        res.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await res.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Ready_needs_a_bff_jwt_unlike_live()
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        (await ClientWithScope(null).GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ClientWithScope("jobs").GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ClientWithScope("bff").GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Business_endpoints_reject_missing_wrong_scope_and_expired_credentials_before_any_logic_runs()
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        StringContent Body() => new("{\"email\":\"a@example.com\",\"password\":\"x\"}", System.Text.Encoding.UTF8, "application/json");

        (await ClientWithScope(null).PostAsync("/api/app/auth/sessions", Body())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ClientWithScope("jobs").PostAsync("/api/app/auth/sessions", Body())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var expired = TestServiceJwt.ValidPayload(now: DateTime.UtcNow.AddMinutes(-10));
        var client = ClientWithScope(null);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestServiceJwt.Create(expired));
        (await client.PostAsync("/api/app/auth/sessions", Body())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Delete })
        {
            (await ClientWithScope(null).SendAsync(new HttpRequestMessage(method, "/api/app/auth/sessions/current")))
                .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await ClientWithScope("jobs").SendAsync(new HttpRequestMessage(method, "/api/app/auth/sessions/current")))
                .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Login_logic_only_runs_after_the_credential_check_passes()
    {
        if (!DbProbe.IsMigratedDatabaseAvailable())
        {
            return;
        }

        // Hệ quả quan trọng: kẻ không có credential dịch vụ không thể làm tăng bộ đếm đăng nhập sai của người khác.
        var email = $"nobody{Guid.NewGuid():N}@example.com";
        StringContent Body() => new($"{{\"email\":\"{email}\",\"password\":\"x\"}}", System.Text.Encoding.UTF8, "application/json");

        (await ClientWithScope(null).PostAsync("/api/app/auth/sessions", Body())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Có credential hợp lệ thì tới được logic đăng nhập và trả 401 chung của "sai thông tin đăng nhập".
        var withCredential = await ClientWithScope("bff").PostAsync("/api/app/auth/sessions", Body());
        withCredential.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await withCredential.Content.ReadAsStringAsync()).ShouldContain("invalid_credentials");
    }
}

/// <summary>Khóa công khai thiếu/sai thì host KHÔNG khởi động.</summary>
public class ServiceCredentialStartupTests
{
    private sealed class FactoryWithPublicKey(string? publicKey) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            EdgeKeyTesting.UseConfiguration(builder, EdgeKeyTesting.Key, publicKey);
        }
    }

    [Fact]
    public void Host_refuses_to_start_with_missing_garbage_private_or_wrong_curve_public_key()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);

        foreach (var bad in new string?[]
                 {
                     null, "", "khong-phai-khoa",
                     TestServiceJwt.Key.ExportPkcs8PrivateKeyPem(),
                     p384.ExportSubjectPublicKeyInfoPem(),
                 })
        {
            using var factory = new FactoryWithPublicKey(bad);

            Should.Throw<Exception>(() => factory.CreateClient());
        }
    }

    [Fact]
    public void Host_starts_with_a_valid_public_key()
    {
        using var factory = new FactoryWithPublicKey(TestServiceJwt.PublicKeyPem);

        Should.NotThrow(() => factory.CreateClient());
    }
}
