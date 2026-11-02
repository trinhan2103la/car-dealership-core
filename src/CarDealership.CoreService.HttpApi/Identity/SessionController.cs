using System.Threading.Tasks;
using CarDealership.CoreService.Controllers;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Auditing;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Ngày 33 (ADR-005): <c>POST /api/app/auth/sessions</c>, chỉ BFF gọi (Service_Credential gắn ở Ngày 37).
/// Thành công 200 + <see cref="LoginResultDto"/>; MỌI thất bại 401 + cùng một body (Req 14.5, 14.6).
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
}

