using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CarDealership.CoreService.Middleware;

/// <summary>
/// Giá trị cấu hình của Edge key: lớp phòng thủ thứ hai, chặn quét host trước cả khi parse JWT.
/// Khóa lấy từ biến môi trường <see cref="VariableName"/>, ngẫu nhiên 32 byte, không bao giờ nằm trong mã nguồn.
/// </summary>
public class EdgeKeyOptions
{
    public const string VariableName = "EDGE_KEY";
    public const int MinLength = 32;

    public string? Key { get; set; }
}

/// <summary>
/// Middleware ĐẦU TIÊN sau Correlation_Id.
/// - Thiếu hoặc sai <c>X-Edge-Key</c> → 404, body rỗng, không header lạ (giống route không tồn tại, không lộ gì).
/// - Ngoại lệ DUY NHẤT: đúng <c>GET /health/live</c> (so khớp nguyên path, không prefix) để health check của host
///   (Koyeb) chạy không cần header. <c>/health/ready</c> vẫn cần key.
/// - So sánh thời gian hằng: băm SHA-256 cả hai phía rồi <see cref="CryptographicOperations.FixedTimeEquals"/> trên 32 byte,
///   nên không lộ độ dài khóa lẫn vị trí ký tự sai đầu tiên.
/// - Log cảnh báo MỖI lần từ chối (lý do, method, path, không query) để thấy được việc quét; KHÔNG bao giờ log giá trị
///   header (cả khóa đúng lẫn khóa người gọi gửi lên, vì người dùng có thể gửi nhầm khóa thật vào chỗ khác).
/// - Đóng mặc định: chưa cấu hình khóa (không thể xảy ra vì khởi động đã kiểm) thì từ chối mọi thứ.
/// </summary>
public class EdgeKeyMiddleware
{
    public const string HeaderName = "X-Edge-Key";
    public const string LivePath = "/health/live";

    private readonly RequestDelegate _next;
    private readonly ILogger<EdgeKeyMiddleware> _logger;
    private readonly byte[]? _expectedHash;

    public EdgeKeyMiddleware(RequestDelegate next, IOptions<EdgeKeyOptions> options, ILogger<EdgeKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;

        var key = options.Value.Key;
        _expectedHash = string.IsNullOrEmpty(key) ? null : Hash(key);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsExemptLive(context.Request))
        {
            await _next(context);
            return;
        }

        var reason = Check(context.Request);
        if (reason is null)
        {
            await _next(context);
            return;
        }

        _logger.LogWarning(
            "Edge key bị từ chối: {Reason}. {Method} {Path}",
            reason,
            context.Request.Method,
            Truncate(context.Request.Path.Value));

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentLength = 0;
    }

    /// <summary>Đúng GET + đúng path /health/live (phân biệt hoa thường, không chấp nhận phần đuôi).</summary>
    internal static bool IsExemptLive(HttpRequest request) =>
        HttpMethods.IsGet(request.Method) && string.Equals(request.Path.Value, LivePath, StringComparison.Ordinal);

    /// <returns>null nếu hợp lệ; ngược lại mã lý do (chỉ để log).</returns>
    private string? Check(HttpRequest request)
    {
        if (_expectedHash is null)
        {
            return "ServerKeyNotConfigured";
        }

        var values = request.Headers[HeaderName];
        if (values.Count == 0)
        {
            return "Missing";
        }

        if (values.Count > 1)
        {
            return "MultipleValues";
        }

        var presented = values[0];
        if (string.IsNullOrEmpty(presented))
        {
            return "Missing";
        }

        return CryptographicOperations.FixedTimeEquals(Hash(presented), _expectedHash) ? null : "Invalid";
    }

    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    private static string Truncate(string? value) =>
        string.IsNullOrEmpty(value) ? "/" : value.Length <= 200 ? value : value[..200];
}
