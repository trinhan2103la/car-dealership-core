using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace CarDealership.CoreService.Identity;

public interface ISessionAppService : IApplicationService
{
    /// <summary>Đăng nhập. Trả null khi thất bại vì BẤT KỲ lý do nào (controller đổi thành 401 chung).</summary>
    Task<LoginResultDto?> LoginAsync(LoginInput input);

    /// <summary>Tra session theo giá trị gốc (header X-Admin-Session), trượt cửa sổ idle. Null nếu không dùng được.</summary>
    Task<CurrentSessionDto?> GetCurrentAsync(string? sessionId);

    /// <summary>Đăng xuất: thu hồi session. False nếu session không dùng được (khi đó không có gì thay đổi).</summary>
    Task<bool> LogoutAsync(string? sessionId);
}

/// <summary>Tra session cho mọi request quản trị (Ngày 37 gắn vào pipeline xác thực để đặt CurrentUser).</summary>
public interface ISessionResolver
{
    Task<ResolvedAdminSession?> ResolveAsync(string? sessionId);
}
