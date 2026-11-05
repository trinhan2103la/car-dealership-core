using System;
using System.Threading.Tasks;
using CarDealership.CoreService.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Auditing;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Chỉ BFF gọi (Service_Credential scope=bff):
/// - <c>POST /api/app/auth/sessions</c>           đăng nhập → refresh token + danh tính; mọi thất bại 401 cùng body.
/// - <c>POST /api/app/auth/sessions/refresh</c>   xoay vòng refresh token (grace 20 s, dùng lại → thu hồi cả family).
/// - <c>POST /api/app/auth/sessions/revoke</c> đăng xuất: thu hồi family của refresh token.
/// - <c>GET  /api/app/auth/sessions/current</c>   danh tính + quyền của phiên trong claim <c>sid</c>/<c>sub</c> của Service_Credential.
/// Refresh token chỉ đi trong BODY (không URL/query/header tự đặt) để không lọt vào log.
/// Mọi phiên không dùng được → 401 + <see cref="SessionFailureDto"/>.
/// [DisableAuditing]: không để ABP auditing ghi body (mật khẩu, refresh token).
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
    [ProducesResponseType(typeof(LoginResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LoginFailureDto), StatusCodes.Status401Unauthorized)]
    public virtual async Task<ObjectResult> LoginAsync([FromBody] LoginInput input)
    {
        var result = await _sessions.LoginAsync(input);

        return result is null
            ? Unauthorized(new LoginFailureDto())
            : Ok(result);
    }

    [HttpPost("refresh")]
    [ProducesResponseType(typeof(LoginResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SessionFailureDto), StatusCodes.Status401Unauthorized)]
    public virtual async Task<ObjectResult> RefreshAsync([FromBody] RefreshTokenInput input)
    {
        var result = await _sessions.RefreshAsync(input.RefreshToken);

        return result is null
            ? Unauthorized(new SessionFailureDto())
            : Ok(result);
    }

    [HttpPost("revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(SessionFailureDto), StatusCodes.Status401Unauthorized)]
    public virtual async Task<ObjectResult> RevokeAsync([FromBody] RefreshTokenInput input)
    {
        return await _sessions.RevokeAsync(input.RefreshToken)
            ? new ObjectResult(null) { StatusCode = 204 }
            : Unauthorized(new SessionFailureDto());
    }

    [HttpGet("current")]
    [ProducesResponseType(typeof(CurrentSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SessionFailureDto), StatusCodes.Status401Unauthorized)]
    public virtual async Task<ObjectResult> GetCurrentAsync()
    {
        var current = await _sessions.GetCurrentAsync(
            ClaimGuid(AdminIdentityClaims.SessionId),
            ClaimGuid(AdminIdentityClaims.UserId));

        return current is null
            ? Unauthorized(new SessionFailureDto())
            : Ok(current);
    }

    private Guid? ClaimGuid(string type) =>
        Guid.TryParse(HttpContext?.User?.FindFirst(type)?.Value, out var value) ? value : null;
}
