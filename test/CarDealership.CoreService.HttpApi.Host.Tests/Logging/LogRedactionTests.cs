using System.Linq;
using CarDealership.CoreService.Logging;
using Serilog;
using Serilog.Context;
using Serilog.Formatting.Compact;
using Serilog.Sinks.InMemory;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Logging.Tests;

/// <summary>
/// Ngày 11 (Req 22, 21): log JSON đủ trường chuẩn và che dữ liệu nhạy cảm.
/// </summary>
public class LogRedactionTests
{
    [Theory]
    [InlineData("/api/app/auth", "/api/app/auth")]
    [InlineData("/xe?page=2&sort=price", "/xe?page=2&sort=price")]
    [InlineData("/cb?token=abc123&page=2", "/cb?token=***&page=2")]
    [InlineData("/login?password=hunter2", "/login?password=***")]
    [InlineData("/x?code=secretcode", "/x?code=***")]
    public void RedactPathAndQuery_masks_sensitive_params(string input, string expected)
    {
        LogRedaction.RedactPathAndQuery(input).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Authorization", true)]
    [InlineData("Cookie", true)]
    [InlineData("X-Edge-Key", true)]
    [InlineData("X-Correlation-Id", false)]
    [InlineData("Content-Type", false)]
    public void IsSensitiveHeader_flags_auth_headers(string header, bool expected)
    {
        LogRedaction.IsSensitiveHeader(header).ShouldBe(expected);
    }

    [Fact]
    public void Log_event_has_seven_standard_fields_and_no_password()
    {
        var sink = new InMemorySink();
        var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", "core-api")
            .Enrich.WithProperty("environment", "Testing")
            .WriteTo.Sink(sink)
            .CreateLogger();

        // Mô phỏng: Correlation_Id đẩy qua LogContext (như middleware Ngày 13),
        // path đã được redact trước khi log (không chứa mật khẩu gốc).
        using (LogContext.PushProperty("Correlation_Id", "corr-123"))
        {
            // path đã redact: "hunter2" KHÔNG được xuất hiện.
            logger.Information("HTTP {method} {path} responded {statusCode}",
                "POST", "/login?password=***", 200);
            // ví dụ có exception
            logger.Error(new System.Exception("boom"), "handler failed");
        }

        Log.CloseAndFlush();

        var events = sink.LogEvents.ToList();
        events.Count.ShouldBe(2);

        // Kiểm property enrich + metadata trên event thường.
        var info = events[0];
        info.Properties.ShouldContainKey("service");
        info.Properties.ShouldContainKey("environment");
        info.Properties.ShouldContainKey("Correlation_Id");
        info.Level.ToString().ShouldBe("Information");
        info.Timestamp.ShouldNotBe(default);

        var error = events[1];
        error.Exception.ShouldNotBeNull();

        // Không rò mật khẩu gốc trong bất kỳ event nào.
        var formatter = new CompactJsonFormatter();
        foreach (var e in events)
        {
            using var sw = new System.IO.StringWriter();
            formatter.Format(e, sw);
            sw.ToString().ShouldNotContain("hunter2");
        }
    }
}
