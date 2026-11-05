using System;
using System.Collections.Generic;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Kết quả đăng nhập / refresh, CHỈ trả cho BFF. BFF đặt <see cref="RefreshToken"/> vào cookie HttpOnly và ký access token
/// (10 phút) chứa <see cref="SessionId"/> (claim <c>sid</c>), <see cref="UserId"/> (claim <c>sub</c>), email, permissions.
/// Refresh token gốc chỉ xuất hiện ở đây; Core_API chỉ giữ SHA-256 của nó. Showroom_Scope thêm ở P3.
/// </summary>
public class LoginResultDto
{
    /// <summary>Refresh token mới. Null khi refresh trong grace 20 s (token cũ vẫn dùng được, BFF KHÔNG đổi cookie).</summary>
    public string? RefreshToken { get; set; }

    /// <summary>Mã family (phiên) — claim <c>sid</c>.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Thời điểm phiên hết hạn tuyệt đối (7 ngày từ lúc đăng nhập). BFF dùng làm Expires của cookie.</summary>
    public DateTime SessionExpiresAt { get; set; }

    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
}

/// <summary>Phản hồi 401 duy nhất cho mọi lý do thất bại: không phân biệt email sai, mật khẩu sai, bị khóa, Inactive.</summary>
public class LoginFailureDto
{
    public const string Code = "invalid_credentials";
    public const string GenericMessage = "Email hoặc mật khẩu không đúng.";

    public string Error { get; set; } = Code;
    public string Message { get; set; } = GenericMessage;
}
