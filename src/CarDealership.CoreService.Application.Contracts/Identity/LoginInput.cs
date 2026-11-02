using System.ComponentModel.DataAnnotations;
using Volo.Abp.Auditing;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Req 14.1: email + mật khẩu. Giới hạn độ dài để chặn input khổng lồ trước khi băm mật khẩu hay ghi DB
/// (mật khẩu hợp lệ tối đa 128, Req 14.3). Lỗi cấu trúc trả 400, KHÔNG tính vào bộ đếm đăng nhập sai.
/// </summary>
public class LoginInput
{
    public const int MaxEmailLength = 320;
    public const int MaxPasswordLength = 128;

    [Required(AllowEmptyStrings = false)]
    [StringLength(MaxEmailLength, MinimumLength = 1)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Req 14.2, 17.5: không bao giờ vào audit log / log.</summary>
    [DisableAuditing]
    [Required(AllowEmptyStrings = false)]
    [StringLength(MaxPasswordLength, MinimumLength = 1)]
    public string Password { get; set; } = string.Empty;
}
