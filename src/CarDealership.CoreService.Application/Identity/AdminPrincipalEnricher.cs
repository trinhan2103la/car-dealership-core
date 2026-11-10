using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;

namespace CarDealership.CoreService.Identity;

/// <summary>Kết quả gắn danh tính Admin_User vào request.</summary>
public enum AdminPrincipalResult
{
    /// <summary>Service JWT không mang sub/sid (gọi không gắn user, ví dụ đăng nhập, public): để nguyên.</summary>
    NoAdminIdentity,

    /// <summary>Phiên hợp lệ: đã thêm claim user id, email và Role của ABP.</summary>
    Enriched,

    /// <summary>Có sub/sid nhưng phiên không dùng được (thu hồi, hết hạn, user Inactive...): request phải bị 401.</summary>
    SessionInvalid,
}

/// <summary>
/// Biến claim <c>sub</c>/<c>sid</c> của Service_Credential thành danh tính ABP cho MỖI request quản trị:
/// tra phiên (thu hồi có hiệu lực ngay), rồi thêm <see cref="AbpClaimTypes.UserId"/>, <see cref="AbpClaimTypes.Email"/> và
/// <see cref="AbpClaimTypes.Role"/>. Nhờ đó <c>ICurrentUser</c> (actor của Audit_Log) và <c>[Authorize(permission)]</c>
/// (RolePermissionValueProvider đọc claim Role) chạy đúng. Core_API là nơi quyết định quyền cuối cùng, không tin Role từ BFF.
/// </summary>
public class AdminPrincipalEnricher : ITransientDependency
{
    public const string AuthenticationType = "CarDealership.AdminSession";

    private readonly ISessionResolver _resolver;
    private readonly IdentityUserManager _userManager;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public AdminPrincipalEnricher(ISessionResolver resolver, IdentityUserManager userManager, IUnitOfWorkManager unitOfWorkManager)
    {
        _resolver = resolver;
        _userManager = userManager;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task<AdminPrincipalResult> EnrichAsync(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirst(AdminIdentityClaims.UserId)?.Value;
        var sid = principal.FindFirst(AdminIdentityClaims.SessionId)?.Value;

        if (sub is null && sid is null)
        {
            return AdminPrincipalResult.NoAdminIdentity;
        }

        if (!Guid.TryParse(sub, out var userId) || !Guid.TryParse(sid, out var sessionId))
        {
            return AdminPrincipalResult.SessionInvalid;
        }

        var resolved = await _resolver.ResolveAsync(sessionId, userId);
        if (resolved is null)
        {
            return AdminPrincipalResult.SessionInvalid;
        }

        IList<string> roles;
        using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            var user = await _userManager.FindByIdAsync(resolved.UserId.ToString());
            if (user is null)
            {
                return AdminPrincipalResult.SessionInvalid;
            }

            roles = await _userManager.GetRolesAsync(user);
            await uow.CompleteAsync();
        }

        var claims = new List<Claim>
        {
            new(AbpClaimTypes.UserId, resolved.UserId.ToString()),
            new(AbpClaimTypes.Email, resolved.Email),
            new(AbpClaimTypes.UserName, resolved.Email),
            new(AbpClaimTypes.SessionId, resolved.SessionId.ToString()),
        };
        claims.AddRange(roles.Distinct(StringComparer.Ordinal).Select(r => new Claim(AbpClaimTypes.Role, r)));

        principal.AddIdentity(new ClaimsIdentity(claims, AuthenticationType, AbpClaimTypes.UserName, AbpClaimTypes.Role));
        return AdminPrincipalResult.Enriched;
    }
}
