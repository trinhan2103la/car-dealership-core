using System;
using System.Linq;
using System.Threading.Tasks;
using CarDealership.CoreService.Identity.Sessions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace CarDealership.CoreService.Identity;

public static class SessionRevokeReasons
{
    public const string Logout = "Logout";
    public const string UserInactive = "UserInactive";
    public const string RefreshTokenReuse = "RefreshTokenReuse";
}

/// <summary>
/// Thu hồi MỌI phiên còn hiệu lực của một user (user bị vô hiệu hóa). Idempotent.
/// Phải gọi trong một unit of work do người gọi quản lý (để cùng giao dịch với việc ghi security event).
/// </summary>
public class SessionRevocation : ITransientDependency
{
    private readonly IRepository<AdminSession, Guid> _sessions;
    private readonly IAsyncQueryableExecuter _executer;

    public SessionRevocation(IRepository<AdminSession, Guid> sessions, IAsyncQueryableExecuter executer)
    {
        _sessions = sessions;
        _executer = executer;
    }

    /// <returns>Số phiên vừa bị thu hồi.</returns>
    public async Task<int> RevokeAllAsync(Guid userId, DateTime nowUtc, string reason = SessionRevokeReasons.UserInactive)
    {
        var query = (await _sessions.GetQueryableAsync()).Where(s => s.UserId == userId && s.RevokedAt == null);
        var active = await _executer.ToListAsync(query);

        foreach (var session in active)
        {
            session.Revoke(nowUtc, reason);
        }

        if (active.Count > 0)
        {
            await _sessions.UpdateManyAsync(active, autoSave: true);
        }

        return active.Count;
    }
}
