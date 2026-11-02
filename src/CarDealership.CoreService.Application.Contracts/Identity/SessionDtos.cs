using System;
using System.Collections.Generic;

namespace CarDealership.CoreService.Identity;

/// <summary>Tên header BFF dùng để chuyển session id mờ sang Core_API (ADR-005).</summary>
public static class AdminSessionHeader
{
    public const string Name = "X-Admin-Session";
}

/// <summary>Kết quả tra session thành công (nhẹ, dùng cho mỗi request quản trị).</summary>
public sealed record ResolvedAdminSession(Guid SessionRecordId, Guid UserId, string Email);

/// <summary>GET /api/app/auth/sessions/current: danh tính + quyền hiện tại (BFF dùng làm mới cache 60 giây).</summary>
public class CurrentSessionDto
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
}

/// <summary>401 cho mọi session không dùng được (thiếu, sai dạng, không tồn tại, thu hồi, hết hạn, user Inactive): không phân biệt.</summary>
public class SessionFailureDto
{
    public const string Code = "unauthenticated";
    public const string GenericMessage = "Phiên đăng nhập không hợp lệ hoặc đã hết hạn.";

    public string Error { get; set; } = Code;
    public string Message { get; set; } = GenericMessage;
}
