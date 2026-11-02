using System;
using System.Collections.Generic;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Kết quả đăng nhập thành công, chỉ trả cho BFF (ADR-005). <see cref="SessionId"/> là giá trị gốc, chỉ xuất hiện
/// ở đây MỘT lần; Core_API chỉ giữ SHA-256 của nó. Showroom_Scope sẽ thêm ở P3 khi có bảng Showroom.
/// </summary>
public class LoginResultDto
{
    public string SessionId { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();
}

/// <summary>Phản hồi 401 duy nhất cho mọi lý do thất bại (Req 14.5, 14.6): không phân biệt email sai, mật khẩu sai, bị khóa, Inactive.</summary>
public class LoginFailureDto
{
    public const string Code = "invalid_credentials";
    public const string GenericMessage = "Email hoặc mật khẩu không đúng.";

    public string Error { get; set; } = Code;
    public string Message { get; set; } = GenericMessage;
}
