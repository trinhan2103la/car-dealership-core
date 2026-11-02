using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CarDealership.CoreService.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Auth.Tests;

/// <summary>
/// Ngày 37 (ADR-004, Req 15.3, 21, 28.12): bảng ca cho xác thực service JWT ES256 + policy theo scope, trên host ASP.NET tối
/// giản (đúng AddServiceCredential + UseAuthentication/UseAuthorization thật, không DB/ABP nên chạy ở mọi máy).
/// </summary>
public class ServiceCredentialTests
{
    private sealed class CapturingProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Lines { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var parts = new List<string> { formatter(state, exception) };
                if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    parts.AddRange(pairs.Select(p => $"{p.Key}={p.Value}"));
                }

                owner.Lines.Enqueue(string.Join(" | ", parts));
            }
        }
    }

    private sealed class Probe : IDisposable
    {
        public IHost Host { get; init; } = null!;
        public CapturingProvider Log { get; init; } = null!;

        public async Task<HttpResponseMessage> GetAsync(string path, string? bearer = null, string scheme = "Bearer")
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (bearer is not null)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue(scheme, bearer);
            }

            return await Host.GetTestClient().SendAsync(request);
        }

        public void Dispose() => Host.Dispose();
    }

    private static async Task<Probe> StartAsync(string? publicKey = null)
    {
        var log = new CapturingProvider();
        var host = await new HostBuilder()
            .ConfigureAppConfiguration(c => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ServiceCredential.PublicKeyVariable] = publicKey ?? TestServiceJwt.PublicKeyPem
            }))
            .ConfigureLogging(l => l.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddProvider(log))
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddServiceCredential();
                    services.AddControllers(options =>
                            options.Conventions.Add(new JobsRouteAuthorizationConvention()))
                        .AddApplicationPart(typeof(ProbeFixtures.AppProbeController).Assembly);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e =>
                    {
                        e.MapControllers();
                        // Giống /health/ready thật: endpoint thường, không khai báo bảo mật -> fallback ServiceBff.
                        e.MapGet("/health/ready", () => "Healthy");
                    });
                }))
            .StartAsync();

        return new Probe { Host = host, Log = log };
    }

    private static Dictionary<string, object> Payload(string scope = "bff") => TestServiceJwt.ValidPayload(scope);

    private static async Task<HttpStatusCode> StatusAsync(string path, string? token, string scheme = "Bearer")
    {
        using var probe = await StartAsync();
        using var response = await probe.GetAsync(path, token, scheme);
        return response.StatusCode;
    }

    // ---------- không có / sai kiểu credential ----------

    [Fact]
    public async Task No_authorization_header_is_401_even_on_the_default_closed_fallback()
    {
        (await StatusAsync("/api/app/probe", null)).ShouldBe(HttpStatusCode.Unauthorized);
        (await StatusAsync("/api/forgot", null)).ShouldBe(HttpStatusCode.Unauthorized);
        (await StatusAsync("/health/ready", null)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("a.b.c")]
    [InlineData("eyJhbGciOiJFUzI1NiJ9.e30.")]
    [InlineData("")]
    public async Task Malformed_tokens_are_401(string token)
    {
        (await StatusAsync("/api/app/probe", token)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Non_bearer_scheme_is_401()
    {
        (await StatusAsync("/api/app/probe", TestServiceJwt.Create(), scheme: "Basic")).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Bearer_scheme_name_is_case_insensitive()
    {
        (await StatusAsync("/api/app/probe", TestServiceJwt.Create(), scheme: "bearer")).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_in_the_query_string_is_not_accepted()
    {
        using var probe = await StartAsync();

        var response = await probe.Host.GetTestClient().GetAsync($"/api/app/probe?access_token={TestServiceJwt.Create()}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- scope ----------

    [Fact]
    public async Task Bff_scope_reaches_normal_endpoints_but_not_jobs()
    {
        var token = TestServiceJwt.Create("bff");

        (await StatusAsync("/api/app/probe", token)).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync("/health/ready", token)).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync("/api/forgot", token)).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync("/api/jobs/probe", token)).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Jobs_scope_reaches_jobs_but_not_normal_endpoints()
    {
        var token = TestServiceJwt.Create("jobs");

        (await StatusAsync("/api/jobs/probe", token)).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync("/api/app/probe", token)).ShouldBe(HttpStatusCode.Forbidden);
        (await StatusAsync("/health/ready", token)).ShouldBe(HttpStatusCode.Forbidden);
        (await StatusAsync("/api/forgot", token)).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("BFF")]
    [InlineData("")]
    [InlineData("bff jobs")]
    public async Task Unknown_or_differently_cased_scope_is_authenticated_but_forbidden(string scope)
    {
        var token = TestServiceJwt.Create(Payload(scope));

        (await StatusAsync("/api/app/probe", token)).ShouldBe(HttpStatusCode.Forbidden);
        (await StatusAsync("/api/jobs/probe", token)).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Missing_scope_claim_is_forbidden_not_unauthorized()
    {
        var payload = Payload();
        payload.Remove("scope");

        (await StatusAsync("/api/app/probe", TestServiceJwt.Create(payload))).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Token_carrying_both_scopes_is_rejected_because_it_would_pass_both_policies()
    {
        var payload = Payload();
        payload["scope"] = new[] { "bff", "jobs" };
        var token = TestServiceJwt.Create(payload);

        (await StatusAsync("/api/app/probe", token)).ShouldBe(HttpStatusCode.Unauthorized);
        (await StatusAsync("/api/jobs/probe", token)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- convention /api/jobs ----------

    [Theory]
    [InlineData("/api/jobs/probe")]
    [InlineData("/api/jobs/sneaky")]              // action ghi đè route tuyệt đối vào /api/jobs
    [InlineData("/API/JOBS/MIXED")]               // hoa/thường
    [InlineData("/api/explicit-jobs")]            // policy gắn tường minh ngoài /api/jobs
    public async Task Job_routes_need_jobs_scope_even_without_an_authorize_attribute(string path)
    {
        (await StatusAsync(path, TestServiceJwt.Create("bff"))).ShouldBe(HttpStatusCode.Forbidden);
        (await StatusAsync(path, TestServiceJwt.Create("jobs"))).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Post_to_a_job_route_is_also_restricted()
    {
        using var probe = await StartAsync();
        var client = probe.Host.GetTestClient();

        async Task<HttpStatusCode> Post(string scope)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/jobs/probe/run");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestServiceJwt.Create(scope));
            return (await client.SendAsync(request)).StatusCode;
        }

        (await Post("bff")).ShouldBe(HttpStatusCode.Forbidden);
        (await Post("jobs")).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_prefix_that_only_looks_like_jobs_is_a_normal_endpoint()
    {
        (await StatusAsync("/api/jobsfoo", TestServiceJwt.Create("bff"))).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync("/api/jobsfoo", TestServiceJwt.Create("jobs"))).ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Allow_anonymous_is_the_only_way_to_open_an_endpoint()
    {
        (await StatusAsync("/api/open", null)).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync("/api/open", "garbage")).ShouldBe(HttpStatusCode.OK);   // token rác không làm hỏng endpoint công khai
    }

    // ---------- thời gian sống ----------

    private static Dictionary<string, object> At(DateTime issuedUtc, int lifetimeSeconds)
    {
        var payload = Payload();
        var iat = new DateTimeOffset(issuedUtc).ToUnixTimeSeconds();
        payload["iat"] = iat;
        payload["exp"] = iat + lifetimeSeconds;
        return payload;
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(59, true)]
    [InlineData(60, true)]      // đúng 60 giây
    [InlineData(61, false)]     // 61 giây: chữ ký đúng vẫn bị từ chối
    [InlineData(120, false)]
    [InlineData(3600, false)]
    public async Task Token_lifetime_is_at_most_60_seconds(int lifetimeSeconds, bool accepted)
    {
        var token = TestServiceJwt.Create(At(DateTime.UtcNow.AddSeconds(-2), lifetimeSeconds));

        (await StatusAsync("/api/app/probe", token))
            .ShouldBe(accepted ? HttpStatusCode.OK : HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public async Task Exp_not_after_iat_is_rejected(int lifetimeSeconds)
    {
        var token = TestServiceJwt.Create(At(DateTime.UtcNow, lifetimeSeconds));

        (await StatusAsync("/api/app/probe", token)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_token_is_401_beyond_the_small_clock_skew_and_accepted_within_it()
    {
        // iat = -62, exp = -2: đã hết hạn 2 giây nhưng trong ClockSkew 5 giây.
        var withinSkew = TestServiceJwt.Create(At(DateTime.UtcNow.AddSeconds(-62), 60));
        // iat = -70, exp = -10: hết hạn 10 giây, vượt skew.
        var beyondSkew = TestServiceJwt.Create(At(DateTime.UtcNow.AddSeconds(-70), 60));
        var longGone = TestServiceJwt.Create(At(DateTime.UtcNow.AddMinutes(-10), 60));

        (await StatusAsync("/api/app/probe", withinSkew)).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync("/api/app/probe", beyondSkew)).ShouldBe(HttpStatusCode.Unauthorized);
        (await StatusAsync("/api/app/probe", longGone)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Validation_parameters_pin_es256_issuer_audience_and_signature_requirements()
    {
        // Thuật toán khác ES256 không thể qua được vì kiểu khóa (EC P-256) đã loại hết; ValidAlgorithms là lớp phòng thủ thứ hai
        // nên các ca tấn công ở trên không tự chứng minh được nó còn đó. Khóa cứng cấu hình ở đây để mutant bỏ nó bị bắt.
        using var probe = await StartAsync();

        var options = probe.Host.Services
            .GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>()
            .Get(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme);
        var parameters = options.TokenValidationParameters;

        parameters.ValidAlgorithms.ShouldBe(new[] { SecurityAlgorithms.EcdsaSha256 });
        parameters.ValidIssuer.ShouldBe("bff");
        parameters.ValidAudience.ShouldBe("core-api");
        parameters.ValidateIssuer.ShouldBeTrue();
        parameters.ValidateAudience.ShouldBeTrue();
        parameters.ValidateLifetime.ShouldBeTrue();
        parameters.RequireExpirationTime.ShouldBeTrue();
        parameters.RequireSignedTokens.ShouldBeTrue();
        parameters.ValidateIssuerSigningKey.ShouldBeTrue();
        parameters.ClockSkew.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(5));
        options.IncludeErrorDetails.ShouldBeFalse();
        options.SaveToken.ShouldBeFalse();
    }

    [Fact]
    public void Clock_skew_is_far_below_the_aspnet_default_of_five_minutes()
    {
        ServiceCredential.ClockSkew.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(10));
        ServiceCredential.MaxLifetime.ShouldBe(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task Token_issued_in_the_future_is_rejected_beyond_the_skew()
    {
        var soon = TestServiceJwt.Create(At(DateTime.UtcNow.AddSeconds(2), 60));        // trong skew
        var future = TestServiceJwt.Create(At(DateTime.UtcNow.AddSeconds(30), 60));     // vượt skew

        (await StatusAsync("/api/app/probe", soon)).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync("/api/app/probe", future)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- claim bắt buộc ----------

    [Theory]
    [InlineData("iat")]
    [InlineData("exp")]
    [InlineData("jti")]
    [InlineData("iss")]
    [InlineData("aud")]
    public async Task Missing_required_claim_is_401(string claim)
    {
        var payload = Payload();
        payload.Remove(claim);

        (await StatusAsync("/api/app/probe", TestServiceJwt.Create(payload))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Empty_or_overlong_jti_is_401_and_128_characters_is_accepted()
    {
        var empty = Payload(); empty["jti"] = "";
        var tooLong = Payload(); tooLong["jti"] = new string('j', ServiceCredential.MaxJtiLength + 1);
        var maximum = Payload(); maximum["jti"] = new string('j', ServiceCredential.MaxJtiLength);

        (await StatusAsync("/api/app/probe", TestServiceJwt.Create(empty))).ShouldBe(HttpStatusCode.Unauthorized);
        (await StatusAsync("/api/app/probe", TestServiceJwt.Create(tooLong))).ShouldBe(HttpStatusCode.Unauthorized);
        (await StatusAsync("/api/app/probe", TestServiceJwt.Create(maximum))).ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("iss", "evil")]
    [InlineData("iss", "BFF")]
    [InlineData("iss", "")]
    [InlineData("aud", "other-api")]
    [InlineData("aud", "Core-API")]
    public async Task Wrong_issuer_or_audience_is_401(string claim, string value)
    {
        var payload = Payload();
        payload[claim] = value;

        (await StatusAsync("/api/app/probe", TestServiceJwt.Create(payload))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- thuật toán / chữ ký ----------

    [Fact]
    public async Task Unsigned_alg_none_token_is_401()
    {
        (await StatusAsync("/api/app/probe", TestServiceJwt.CreateUnsigned(Payload()))).ShouldBe(HttpStatusCode.Unauthorized);
        (await StatusAsync("/api/jobs/probe", TestServiceJwt.CreateUnsigned(Payload("jobs")))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Hs256_token_signed_with_the_public_key_as_hmac_secret_is_401()
    {
        // Tấn công nhầm thuật toán: lấy KHÓA CÔNG KHAI (ai cũng có) làm bí mật HMAC.
        var secret = TestServiceJwt.Key.ExportSubjectPublicKeyInfo();
        var credentials = new SigningCredentials(new SymmetricSecurityKey(secret), SecurityAlgorithms.HmacSha256);

        (await StatusAsync("/api/app/probe", TestServiceJwt.Create(Payload(), credentials))).ShouldBe(HttpStatusCode.Unauthorized);
        (await StatusAsync("/api/jobs/probe", TestServiceJwt.Create(Payload("jobs"), credentials))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Hs256_token_signed_with_pem_text_as_hmac_secret_is_401()
    {
        var secret = Encoding.UTF8.GetBytes(TestServiceJwt.PublicKeyPem);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(secret), SecurityAlgorithms.HmacSha256);

        (await StatusAsync("/api/app/probe", TestServiceJwt.Create(Payload(), credentials))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Es256_token_signed_by_a_different_key_is_401()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var token = TestServiceJwt.Create(Payload(), TestServiceJwt.Es256(attacker));

        (await StatusAsync("/api/app/probe", token)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Es384_token_is_401_even_if_correctly_signed_by_a_p384_key()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var credentials = new SigningCredentials(new ECDsaSecurityKey(p384), SecurityAlgorithms.EcdsaSha384);

        (await StatusAsync("/api/app/probe", TestServiceJwt.Create(Payload(), credentials))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rs256_token_is_401()
    {
        using var rsa = RSA.Create(2048);
        var credentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);

        (await StatusAsync("/api/app/probe", TestServiceJwt.Create(Payload(), credentials))).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tampering_with_the_payload_after_signing_is_401()
    {
        var parts = TestServiceJwt.Create("bff").Split('.');
        var forged = Base64UrlEncoder.Encode(System.Text.Json.JsonSerializer.Serialize(Payload("jobs")));

        var token = $"{parts[0]}.{forged}.{parts[2]}";

        (await StatusAsync("/api/jobs/probe", token)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Truncated_signature_is_401()
    {
        var token = TestServiceJwt.Create("bff");

        (await StatusAsync("/api/app/probe", token[..^6])).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_valid_token_can_be_presented_more_than_once_within_its_lifetime()
    {
        // Ghi nhận giới hạn: chưa chống phát lại theo jti trong 60 giây (token chỉ sống 60s và đi qua kênh TLS giữa hai dịch vụ).
        var token = TestServiceJwt.Create("bff");

        (await StatusAsync("/api/app/probe", token)).ShouldBe(HttpStatusCode.OK);
        (await StatusAsync("/api/app/probe", token)).ShouldBe(HttpStatusCode.OK);
    }

    // ---------- phản hồi và log ----------

    [Fact]
    public async Task Rejection_reveals_nothing_in_headers_or_body()
    {
        using var probe = await StartAsync();

        var expired = await probe.GetAsync("/api/app/probe", TestServiceJwt.Create(At(DateTime.UtcNow.AddMinutes(-10), 60)));
        var noToken = await probe.GetAsync("/api/app/probe");

        foreach (var response in new[] { expired, noToken })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
            string.Join(",", response.Headers.WwwAuthenticate.Select(h => h.ToString())).ShouldBe("Bearer");
        }
    }

    [Fact]
    public async Task Rejected_tokens_are_logged_by_error_type_only_never_the_token_or_its_claims()
    {
        using var probe = await StartAsync();
        var payload = Payload();
        payload["jti"] = "jti-secret-marker-1234567890";
        payload["cid"] = "cid-secret-marker-1234567890";
        payload["exp"] = (long)payload["iat"] - 600;     // đã hết hạn -> bị từ chối
        var token = TestServiceJwt.Create(payload);

        (await probe.GetAsync("/api/app/probe", token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var all = string.Join("\n", probe.Log.Lines);
        all.ShouldContain("SecurityTokenExpiredException");
        all.ShouldNotContain(token);
        all.ShouldNotContain(token.Split('.')[1]);
        all.ShouldNotContain("jti-secret-marker");
        all.ShouldNotContain("cid-secret-marker");
    }

    [Fact]
    public async Task Policy_rejection_for_a_valid_token_with_a_lifetime_over_60_seconds_is_logged_with_a_reason_code()
    {
        using var probe = await StartAsync();
        var token = TestServiceJwt.Create(At(DateTime.UtcNow.AddSeconds(-2), 3600));

        (await probe.GetAsync("/api/app/probe", token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        string.Join("\n", probe.Log.Lines).ShouldContain("LifetimeTooLong");
    }

    // ---------- cấu hình khóa ----------

    [Fact]
    public async Task Without_a_usable_public_key_every_token_is_rejected()
    {
        using var probe = await StartAsync(publicKey: "khong-phai-khoa");

        using var response = await probe.GetAsync("/api/app/probe", TestServiceJwt.Create());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_for_a_different_deployment_key_is_rejected_when_the_key_is_rotated()
    {
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var probe = await StartAsync(publicKey: other.ExportSubjectPublicKeyInfoPem());

        using var response = await probe.GetAsync("/api/app/probe", TestServiceJwt.Create());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
