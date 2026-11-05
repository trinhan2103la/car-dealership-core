using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Context;

namespace CarDealership.CoreService.Middleware;

/// <summary>
/// Chuẩn hóa Correlation_Id.
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

        // Đẩy cả vào ICorrelationIdProvider của ABP để security log / audit lấy đúng Correlation_Id
        // (middleware của ta thay UseCorrelationId của ABP nên provider này không tự được đặt).
        // Host tối giản trong test không có ABP thì bỏ qua.
        var abpProvider = context.RequestServices.GetService<Volo.Abp.Tracing.ICorrelationIdProvider>();
        using var abpScope = abpProvider?.Change(correlationId);

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
