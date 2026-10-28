using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace CarDealership.CoreService.Middleware;

/// <summary>
/// Ngày 13 (Req 22.2–22.5): chuẩn hóa Correlation_Id.
/// - Nhận header "X-Correlation-Id"; nếu hợp lệ (^[A-Za-z0-9_-]{1,128}$) thì dùng lại.
/// - Nếu thiếu hoặc không hợp lệ thì sinh mới (tránh log injection).
/// - Đẩy vào Serilog LogContext (property "Correlation_Id") để xuất ra mọi dòng log.
/// - Trả lại trong response header "X-Correlation-Id".
/// Đặt middleware này ở ĐẦU pipeline (trước Serilog request logging).
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    private static readonly Regex Valid = new(@"^[A-Za-z0-9_-]{1,128}$", RegexOptions.Compiled);

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = (!string.IsNullOrEmpty(incoming) && Valid.IsMatch(incoming))
            ? incoming
            : GenerateId();

        // Trả lại cho client (set trước khi response bắt đầu gửi).
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("Correlation_Id", correlationId))
        {
            await _next(context);
        }
    }

    private static string GenerateId()
    {
        // 32 ký tự hex, thuộc tập [A-Za-z0-9_-], hợp lệ với regex.
        return System.Guid.NewGuid().ToString("N");
    }
}
