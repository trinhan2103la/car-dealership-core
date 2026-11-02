using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Middleware.Tests;

/// <summary>
/// Ngày 36 (Req 28.12, 21): hợp đồng của EdgeKeyMiddleware trên pipeline ASP.NET thật (TestServer, không DB/ABP).
/// </summary>
public class EdgeKeyMiddlewareTests
{
    // Ngẫu nhiên mỗi lần chạy: không để chuỗi giống bí mật trong mã nguồn (gitleaks).
    private static readonly string Key = "unit-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    private sealed class CapturedLog
    {
        public string Category { get; init; } = string.Empty;
        public LogLevel Level { get; init; }
        public string Text { get; init; } = string.Empty;
    }

    private sealed class CapturingProvider : ILoggerProvider
    {
        /// <summary>Mọi log của mọi category (gồm cả framework).</summary>
        public ConcurrentQueue<CapturedLog> AllLogs { get; } = new();

        /// <summary>Chỉ log của EdgeKeyMiddleware (framework ở mức Trace cũng log cả URL + query của request).</summary>
        public IEnumerable<CapturedLog> Logs => AllLogs.Where(l => l.Category.EndsWith(nameof(EdgeKeyMiddleware), StringComparison.Ordinal));

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                // Gom cả thông điệp đã format LẪN mọi cặp giá trị có cấu trúc (state) để chắc không giá trị nào lọt ra.
                var parts = new List<string> { formatter(state, exception) };
                if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    parts.AddRange(pairs.Select(p => $"{p.Key}={p.Value}"));
                }

                owner.AllLogs.Enqueue(new CapturedLog { Category = category, Level = logLevel, Text = string.Join(" | ", parts) });
            }
        }
    }

    // Dấu hiệu "dùng khóa mặc định của test" (tham số mặc định phải là hằng số, mà Key giờ sinh ngẫu nhiên lúc chạy).
    private const string UseDefaultKey = "\u0000use-default-key";

    private sealed record Result(HttpResponseMessage Response, string Body, CapturingProvider Log);

    private static async Task<Result> SendAsync(
        HttpMethod method,
        string pathAndQuery,
        Action<HttpRequestMessage>? configure = null,
        string? serverKey = UseDefaultKey)
    {
        var provider = new CapturingProvider();
        using var host = await new HostBuilder()
            .ConfigureLogging(l => l.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddProvider(provider))
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(s => s.Configure<EdgeKeyOptions>(o => o.Key = serverKey == UseDefaultKey ? Key : serverKey))
                .Configure(app =>
                {
                    app.UseMiddleware<EdgeKeyMiddleware>();
                    app.Run(async ctx => await ctx.Response.WriteAsync("passed"));
                }))
            .StartAsync();

        var request = new HttpRequestMessage(method, pathAndQuery);
        configure?.Invoke(request);
        var response = await host.GetTestClient().SendAsync(request);
        return new Result(response, await response.Content.ReadAsStringAsync(), provider);
    }

    private static Action<HttpRequestMessage> WithKey(string key) =>
        r => r.Headers.TryAddWithoutValidation(EdgeKeyMiddleware.HeaderName, key);

    private static void ShouldBeSilent404(Result result)
    {
        result.Response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        result.Body.ShouldBeEmpty();
        result.Response.Content.Headers.ContentLength.GetValueOrDefault().ShouldBe(0);
        result.Response.Headers.Contains("WWW-Authenticate").ShouldBeFalse();
    }

    // ---------- khóa ----------

    [Fact]
    public async Task Correct_key_passes_through()
    {
        var result = await SendAsync(HttpMethod.Get, "/api/anything", WithKey(Key));

        result.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        result.Body.ShouldBe("passed");
        result.Log.Logs.ShouldBeEmpty();   // đường bình thường không log gì
    }

    [Fact]
    public async Task Missing_key_is_a_silent_404()
    {
        var result = await SendAsync(HttpMethod.Get, "/api/anything");

        ShouldBeSilent404(result);
    }

    public static IEnumerable<object[]> WrongKeys => new[]
    {
        new object[] { "wrong" },
        new object[] { Key + "x" },                              // dài hơn
        new object[] { Key[..^1] },                              // ngắn hơn một ký tự
        new object[] { Key[..^1] + "X" },                        // cùng độ dài, sai ký tự cuối
        new object[] { "X" + Key[1..] },                         // cùng độ dài, sai ký tự đầu
        new object[] { Key.ToUpperInvariant() },                 // phân biệt hoa thường
        new object[] { " " + Key },                              // khoảng trắng đầu
        new object[] { Key + " " },                              // khoảng trắng cuối
        new object[] { new string('a', 4096) },                  // rất dài
    };

    [Theory]
    [MemberData(nameof(WrongKeys))]
    public async Task Wrong_key_is_a_silent_404(string presented)
    {
        var result = await SendAsync(HttpMethod.Get, "/api/anything", WithKey(presented));

        ShouldBeSilent404(result);
    }

    [Fact]
    public async Task Empty_key_header_is_a_silent_404()
    {
        var result = await SendAsync(HttpMethod.Get, "/api/anything", WithKey(string.Empty));

        ShouldBeSilent404(result);
    }

    [Fact]
    public async Task Two_values_of_the_header_are_rejected_even_if_one_is_correct()
    {
        var result = await SendAsync(HttpMethod.Get, "/api/anything",
            r => r.Headers.TryAddWithoutValidation(EdgeKeyMiddleware.HeaderName, new[] { Key, Key }));

        ShouldBeSilent404(result);
    }

    [Fact]
    public async Task Key_in_the_query_string_or_other_header_does_not_count()
    {
        var viaQuery = await SendAsync(HttpMethod.Get, $"/api/anything?X-Edge-Key={Key}");
        var viaOtherHeader = await SendAsync(HttpMethod.Get, "/api/anything",
            r => r.Headers.TryAddWithoutValidation("X-Api-Key", Key));

        ShouldBeSilent404(viaQuery);
        ShouldBeSilent404(viaOtherHeader);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("GET")]
    public async Task Every_method_needs_the_key(string method)
    {
        ShouldBeSilent404(await SendAsync(new HttpMethod(method), "/api/anything"));
        (await SendAsync(new HttpMethod(method), "/api/anything", WithKey(Key))).Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Header_name_is_case_insensitive_but_value_is_not()
    {
        var result = await SendAsync(HttpMethod.Get, "/api/anything",
            r => r.Headers.TryAddWithoutValidation("x-edge-key", Key));

        result.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Server_without_a_configured_key_rejects_everything_even_an_empty_or_guessed_key()
    {
        ShouldBeSilent404(await SendAsync(HttpMethod.Get, "/api/anything", serverKey: null));
        ShouldBeSilent404(await SendAsync(HttpMethod.Get, "/api/anything", WithKey(string.Empty), serverKey: null));
        ShouldBeSilent404(await SendAsync(HttpMethod.Get, "/api/anything", WithKey(Key), serverKey: string.Empty));
    }

    // ---------- /health/live ----------

    [Fact]
    public async Task Get_health_live_is_exempt_and_needs_no_key()
    {
        var result = await SendAsync(HttpMethod.Get, "/health/live");

        result.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
        result.Log.Logs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/health/ready")]
    [InlineData("/health")]
    [InlineData("/health/live/")]
    [InlineData("/health/live/extra")]
    [InlineData("/health/liveness")]
    [InlineData("/Health/Live")]
    [InlineData("/health/live%2F")]
    [InlineData("//health/live")]
    [InlineData("/api/../health/ready")]
    public async Task Only_the_exact_live_path_is_exempt_everything_else_needs_the_key(string path)
    {
        var withoutKey = await SendAsync(HttpMethod.Get, path);

        ShouldBeSilent404(withoutKey);
    }

    [Fact]
    public async Task Exemption_is_only_for_GET_not_for_other_methods_on_the_live_path()
    {
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Options })
        {
            ShouldBeSilent404(await SendAsync(method, "/health/live"));
        }
    }

    [Fact]
    public async Task Live_path_with_a_query_string_is_still_the_live_path()
    {
        // Query không thuộc path; endpoint health check vẫn chỉ trả 200 rỗng, không lộ gì thêm.
        var result = await SendAsync(HttpMethod.Get, "/health/live?probe=1");

        result.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ready_with_the_key_passes()
    {
        var result = await SendAsync(HttpMethod.Get, "/health/ready", WithKey(Key));

        result.Response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---------- log ----------

    [Fact]
    public async Task Rejection_is_logged_as_a_warning_with_reason_method_and_path()
    {
        var result = await SendAsync(HttpMethod.Post, "/api/app/auth/sessions", WithKey("bad-key-value"));

        var log = result.Log.Logs.ShouldHaveSingleItem();
        log.Level.ShouldBe(LogLevel.Warning);
        log.Text.ShouldContain("Invalid");
        log.Text.ShouldContain("POST");
        log.Text.ShouldContain("/api/app/auth/sessions");
    }

    [Theory]
    [InlineData(null, "Missing")]
    [InlineData("", "Missing")]
    [InlineData("nope", "Invalid")]
    public async Task Reason_codes_distinguish_missing_from_invalid(string? presented, string expectedReason)
    {
        var result = await SendAsync(HttpMethod.Get, "/api/anything", presented is null ? null : WithKey(presented));

        result.Log.Logs.Single().Text.ShouldContain(expectedReason);
    }

    [Fact]
    public async Task Logs_never_contain_the_expected_key_nor_the_presented_key_nor_the_query()
    {
        const string presented = "presented-secret-value-do-not-log-0123456789";

        var result = await SendAsync(HttpMethod.Get, $"/api/anything?token=query-secret-123&x={Key}", WithKey(presented));

        var all = string.Join("\n", result.Log.Logs.Select(l => l.Text));
        all.ShouldNotBeEmpty();
        all.ShouldNotContain(Key);
        all.ShouldNotContain(presented);
        all.ShouldNotContain("query-secret-123");
        all.ShouldNotContain("token=");
    }

    [Fact]
    public async Task A_correct_key_sent_to_a_wrong_server_is_not_logged_either()
    {
        // Người gọi cấu hình nhầm khóa thật của dịch vụ khác: giá trị vẫn không được vào log.
        var result = await SendAsync(HttpMethod.Get, "/api/anything", WithKey(Key), serverKey: "a-completely-different-server-key-0123456789");

        var all = string.Join("\n", result.Log.Logs.Select(l => l.Text));
        all.ShouldNotContain(Key);
        all.ShouldNotContain("a-completely-different-server-key");
    }

    [Fact]
    public async Task Very_long_paths_are_truncated_in_the_log()
    {
        var result = await SendAsync(HttpMethod.Get, "/" + new string('a', 5000));

        result.Log.Logs.Single().Text.Length.ShouldBeLessThan(1000);
    }

    [Fact]
    public void Multiple_values_reason_is_reported()
    {
        var task = SendAsync(HttpMethod.Get, "/api/anything",
            r => r.Headers.TryAddWithoutValidation(EdgeKeyMiddleware.HeaderName, new[] { "a", "b" }));

        task.GetAwaiter().GetResult().Log.Logs.Single().Text.ShouldContain("MultipleValues");
    }

    [Fact]
    public async Task Server_without_key_logs_the_misconfiguration_reason()
    {
        var result = await SendAsync(HttpMethod.Get, "/api/anything", WithKey(Key), serverKey: null);

        result.Log.Logs.Single().Text.ShouldContain("ServerKeyNotConfigured");
    }
}
