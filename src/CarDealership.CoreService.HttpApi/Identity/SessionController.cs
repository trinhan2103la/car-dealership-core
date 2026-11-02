using System.Threading.Tasks;
using CarDealership.CoreService.Controllers;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Auditing;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Ngày 33–34 (ADR-005), chỉ BFF gọi (Service_Credential gắn ở Ngày 37):
/// - <c>POST   /api/app/auth/sessions</c>          đăng nhập; mọi thất bại 401 cùng một body (Req 14.5, 14.6).
/// - <c>GET    /api/app/auth/sessions/current</c>   danh tính + quyền hiện tại, trượt cửa sổ idle 30 phút (Req 14.7).
/// - <c>DELETE /api/app/auth/sessions/current</c>   đăng xuất, thu hồi session phía server (Req 14.8).
/// Session id gốc đi qua header <c>X-Admin-Session</c> (không đi qua URL/query để khỏi lọt vào log).
/// Mọi session không dùng được → 401 + <see cref="SessionFailureDto"/>.
/// [DisableAuditing]: không để ABP auditing ghi body (mật khẩu, session id).
/// </summary>
[Route("api/app/auth/sessions")]
[DisableAuditing]
public class SessionController : CoreServiceController
{
    private readonly ISessionAppService _sessions;

    public SessionController(ISessionAppService sessions)
    {
        _sessions = sessions;
    }

    // Trả ObjectResult (không phải IActionResult) để AbpExceptionFilter luôn xử lý lỗi validation thành 400 JSON,
    // kể cả khi client không gửi header Accept: application/json.
    [HttpPost]
    public virtual async Task<ObjectResult> LoginAsync([FromBody] LoginInput input)
    {
        var result = await _sessions.LoginAsync(input);

        return result is null
            ? Unauthorized(new LoginFailureDto())
            : Ok(result);
    }

    [HttpGet("current")]
    public virtual async Task<ObjectResult> GetCurrentAsync([FromHeader(Name = AdminSessionHeader.Name)] string? sessionId)
    {
        var current = await _sessions.GetCurrentAsync(sessionId);

        return current is null
            ? Unauthorized(new SessionFailureDto())
            : Ok(current);
    }

    [HttpDelete("current")]
    public virtual async Task<ObjectResult> LogoutAsync([FromHeader(Name = AdminSessionHeader.Name)] string? sessionId)
    {
        var loggedOut = await _sessions.LogoutAsync(sessionId);

        return loggedOut
            ? new ObjectResult(null) { StatusCode = 204 }
            : Unauthorized(new SessionFailureDto());
    }
}
