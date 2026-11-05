using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace CarDealership.CoreService.Identity;

/// <summary>Vòng đời phiên Admin_User = family refresh token. Mọi thất bại trả null/false (controller đổi thành 401 chung).</summary>
public interface ISessionAppService : IApplicationService
{
    /// <summary>Đăng nhập: tạo family + refresh token đầu tiên.</summary>
    Task<LoginResultDto?> LoginAsync(LoginInput input);

    /// <summary>
    /// Đổi refresh token lấy token mới (xoay vòng). Token vừa bị thay trong grace 20 s: chấp nhận, KHÔNG xoay
    /// (<see cref="LoginResultDto.RefreshToken"/> = null). Dùng lại token cũ ngoài grace: thu hồi CẢ family.
    /// </summary>
    Task<LoginResultDto?> RefreshAsync(string? refreshToken);

    /// <summary>Đăng xuất: thu hồi family của refresh token (hiện hành hoặc vừa bị thay). False nếu không có gì để thu hồi.</summary>
    Task<bool> RevokeAsync(string? refreshToken);

    /// <summary>Danh tính + quyền của phiên <paramref name="sessionId"/> thuộc <paramref name="userId"/> (claim sid/sub). Không ghi gì.</summary>
    Task<CurrentSessionDto?> GetCurrentAsync(Guid? sessionId, Guid? userId);
}

/// <summary>Tra phiên cho mọi request quản trị theo claim sid/sub của Service_Credential (thu hồi có hiệu lực ngay).</summary>
public interface ISessionResolver
{
    Task<ResolvedAdminSession?> ResolveAsync(Guid? sessionId, Guid? userId);
}
