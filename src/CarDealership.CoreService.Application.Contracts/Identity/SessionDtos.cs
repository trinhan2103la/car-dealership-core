using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Auditing;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Claim danh tính Admin_User trong Service_Credential (JWT BFF→Core_API). BFF lấy từ access token đã kiểm.
/// </summary>
public static class AdminIdentityClaims
{
    public const string UserId = "sub";
    public const string SessionId = "sid";
}

/// <summary>Kết quả tra phiên thành công (nhẹ, dùng cho mỗi request quản trị).</summary>
public sealed record ResolvedAdminSession(Guid SessionId, Guid UserId, string Email);

/// <summary>POST /api/app/auth/sessions/refresh và /revoke: refresh token gốc (BFF đọc từ cookie HttpOnly).</summary>
public class RefreshTokenInput
{
    public const int MaxLength = 256;

    /// <summary>Không bao giờ vào audit log / log.</summary>
    [DisableAuditing]
    [Required(AllowEmptyStrings = false)]
    [StringLength(MaxLength, MinimumLength = 1)]
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>GET /api/app/auth/sessions/current: danh tính + quyền hiện tại của phiên trong claim sid/sub.</summary>
public class CurrentSessionDto
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
}

/// <summary>401 cho mọi phiên không dùng được (thiếu, sai dạng, không tồn tại, thu hồi, hết hạn, dùng lại, user Inactive): không phân biệt.</summary>
public class SessionFailureDto
{
    public const string Code = "unauthenticated";
    public const string GenericMessage = "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.";

    public string Error { get; set; } = Code;
    public string Message { get; set; } = GenericMessage;
}
