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
/// ADR-005, Req 14.7–14.9: tra session theo session id gốc cho mỗi request quản trị.
/// - Chỉ chấp nhận chuỗi đúng dạng 43 ký tự base64url (rác bị loại trước khi chạm DB).
/// - Tra bằng SHA-256; thu hồi hoặc quá 30 phút không hoạt động thì từ chối và KHÔNG ghi gì.
/// - User bị Inactive/xóa: thu hồi mọi session của user đó rồi từ chối (hiệu lực ngay, tốt hơn mức 5 phút của Req 14.9).
/// - Thành công thì trượt <c>last_seen_at</c> về hiện tại.
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

    public async Task<ResolvedAdminSession?> ResolveAsync(string? sessionId)
    {
        if (!SessionToken.IsWellFormed(sessionId))
        {
            return null;
        }

        var now = _clock.Now;

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);

        var session = await FindAsync(sessionId!);
        if (session is null || !session.IsActiveAt(now))
        {
            return null;   // không hoàn tất UoW: không ghi gì
        }

        var user = await _userManager.FindByIdAsync(session.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            var revoked = await _revocation.RevokeAllAsync(session.UserId, now);
            await _events.WriteAsync(SecurityEventActions.SessionsRevoked, user?.Email, session.UserId,
                revoked > 0 ? "UserInactive" : "UserInactiveNoActiveSession");
            await uow.CompleteAsync();
            return null;
        }

        session.Touch(now);
        await _sessions.UpdateAsync(session, autoSave: true);
        await uow.CompleteAsync();

        return new ResolvedAdminSession(session.Id, user.Id, user.Email);
    }

    /// <summary>Tra theo hash trong UoW hiện tại (entity được theo dõi để cập nhật).</summary>
    internal async Task<AdminSession?> FindAsync(string sessionId)
    {
        var hash = SessionToken.Hash(sessionId);
        var query = (await _sessions.GetQueryableAsync()).Where(s => s.TokenHash == hash);
        return await _executer.FirstOrDefaultAsync(query);
    }
}
