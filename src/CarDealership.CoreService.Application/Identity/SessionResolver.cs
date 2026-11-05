using System;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Identity.Sessions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Linq;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Tra phiên cho mỗi request quản trị theo claim <c>sid</c> + <c>sub</c> của Service_Credential.
/// - Tra theo khóa chính (rẻ); KHÔNG ghi gì khi thành công (idle 3 ngày tính theo lần refresh, không theo từng request).
/// - Phiên thu hồi / hết hạn / không thuộc user trong claim → từ chối. Nhờ vậy đăng xuất hay phát hiện dùng lại refresh
///   token có hiệu lực NGAY, dù access token (10 phút) còn hạn.
/// - User bị Inactive/xóa: thu hồi mọi phiên của user rồi từ chối (hiệu lực ngay).
/// Mọi lý do thất bại cho cùng một kết quả null.
/// </summary>
public class SessionResolver : ISessionResolver, ITransientDependency
{
    private readonly IRepository<AdminSession, Guid> _sessions;
    private readonly IAsyncQueryableExecuter _executer;
    private readonly IdentityUserManager _userManager;
    private readonly SessionRevocation _revocation;
    private readonly ISecurityEventWriter _events;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IClock _clock;

    public SessionResolver(
        IRepository<AdminSession, Guid> sessions,
        IAsyncQueryableExecuter executer,
        IdentityUserManager userManager,
        SessionRevocation revocation,
        ISecurityEventWriter events,
        IUnitOfWorkManager unitOfWorkManager,
        IClock clock)
    {
        _sessions = sessions;
        _executer = executer;
        _userManager = userManager;
        _revocation = revocation;
        _events = events;
        _unitOfWorkManager = unitOfWorkManager;
        _clock = clock;
    }

    public async Task<ResolvedAdminSession?> ResolveAsync(Guid? sessionId, Guid? userId)
    {
        if (sessionId is null || userId is null || sessionId == Guid.Empty || userId == Guid.Empty)
        {
            return null;
        }

        var now = _clock.Now;

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);

        var query = (await _sessions.GetQueryableAsync()).Where(s => s.Id == sessionId.Value);
        var session = await _executer.FirstOrDefaultAsync(query);
        if (session is null || session.UserId != userId.Value || !session.IsActiveAt(now))
        {
            return null;   // không hoàn tất UoW: không ghi gì
        }

        var user = await _userManager.FindByIdAsync(session.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            await RevokeInactiveAsync(session.UserId, user?.Email, now);
            await uow.CompleteAsync();
            return null;
        }

        return new ResolvedAdminSession(session.Id, user.Id, user.Email);
    }

    /// <summary>Thu hồi mọi phiên của user Inactive + ghi security event. Gọi trong UoW của người gọi.</summary>
    internal async Task RevokeInactiveAsync(Guid userId, string? email, DateTime now)
    {
        var revoked = await _revocation.RevokeAllAsync(userId, now, SessionRevokeReasons.UserInactive);
        await _events.WriteAsync(SecurityEventActions.SessionsRevoked, email, userId,
            revoked > 0 ? "UserInactive" : "UserInactiveNoActiveSession");
    }

    /// <summary>Tìm family theo hash của refresh token hiện hành HOẶC token vừa bị thay (trong UoW hiện tại, entity được theo dõi).</summary>
    internal async Task<AdminSession?> FindByRefreshTokenAsync(byte[] hash)
    {
        var query = (await _sessions.GetQueryableAsync())
            .Where(s => s.TokenHash == hash || s.PreviousTokenHash == hash);
        return await _executer.FirstOrDefaultAsync(query);
    }
}
