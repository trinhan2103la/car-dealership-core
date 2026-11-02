using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CarDealership.CoreService.Identity.Login;
using CarDealership.CoreService.Identity.Sessions;
using Microsoft.AspNetCore.Identity;
using Volo.Abp.Auditing;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;
using AbpIdentityUser = Volo.Abp.Identity.IdentityUser;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Ngày 33 (Req 14.1, 14.4, 14.5, 14.6, 14.9, 17): đăng nhập, áp <see cref="LoginAttemptPolicy"/>, tạo session, ghi security event.
///
/// Quyết định thiết kế:
/// - KHÔNG ném exception khi thất bại: ném sẽ rollback unit of work bao ngoài và lần sai không được ghi, nên
///   bộ đếm không bao giờ tăng. Thất bại trả null; việc ghi lần đăng nhập + security event nằm trong UoW riêng (requiresNew).
/// - Mọi lý do thất bại (email lạ, sai mật khẩu, đang khóa, Inactive) cho cùng một kết quả ra ngoài; lý do chỉ có trong log nội bộ.
/// - Cả nhánh "không kiểm mật khẩu thật" vẫn băm một mật khẩu giả để thời gian phản hồi không lộ email có tồn tại.
/// - Tuần tự hóa theo email (cổng khóa trong tiến trình): nếu không, N request song song cùng đọc "chưa khóa" và vượt
///   quá 5 lần thử. Giả định MVP chạy MỘT instance Core_API (ADR-007, không Redis); nhiều instance cần khóa ở DB.
/// - [DisableAuditing]: ABP auditing ghi tham số action, sẽ đưa mật khẩu và session id vào AbpAuditLogActions.
/// </summary>
[DisableAuditing]
public class SessionAppService : CoreServiceAppService, ISessionAppService
{
    private static readonly TimeSpan HistoryWindow = TimeSpan.FromHours(1);

    private const int GateCount = 64;
    private static readonly SemaphoreSlim[] Gates =
        Enumerable.Range(0, GateCount).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<AbpIdentityUser>().HashPassword(
            new AbpIdentityUser(Guid.NewGuid(), "dummy", "dummy@example.invalid"),
            Guid.NewGuid().ToString("N") + "Aa1!"));

    private readonly IdentityUserManager _userManager;
    private readonly IPasswordHasher<AbpIdentityUser> _passwordHasher;
    private readonly IRepository<LoginAttempt, Guid> _attempts;
    private readonly IRepository<AdminSession, Guid> _sessions;
    private readonly IPermissionManager _permissionManager;
    private readonly ISecurityEventWriter _events;
    private readonly SessionResolver _resolver;

    public SessionAppService(
        IdentityUserManager userManager,
        IPasswordHasher<AbpIdentityUser> passwordHasher,
        IRepository<LoginAttempt, Guid> attempts,
        IRepository<AdminSession, Guid> sessions,
        IPermissionManager permissionManager,
        ISecurityEventWriter events,
        SessionResolver resolver)
    {
        _userManager = userManager;
        _passwordHasher = passwordHasher;
        _attempts = attempts;
        _sessions = sessions;
        _permissionManager = permissionManager;
        _events = events;
        _resolver = resolver;
    }

    public virtual async Task<LoginResultDto?> LoginAsync(LoginInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var email = input.Email.Trim();
        var normalized = LoginEmail.Normalize(email);

        var gate = Gates[(uint)normalized.GetHashCode() % GateCount];
        await gate.WaitAsync();
        try
        {
            return await LoginCoreAsync(email, normalized, input.Password);
        }
        finally
        {
            gate.Release();
        }
    }

    public virtual async Task<CurrentSessionDto?> GetCurrentAsync(string? sessionId)
    {
        var resolved = await _resolver.ResolveAsync(sessionId);
        if (resolved is null)
        {
            return null;
        }

        var user = await _userManager.FindByIdAsync(resolved.UserId.ToString());
        if (user is null)
        {
            return null;
        }

        var roles = (await _userManager.GetRolesAsync(user)).OrderBy(r => r, StringComparer.Ordinal).ToList();

        return new CurrentSessionDto
        {
            UserId = user.Id,
            Email = user.Email,
            Roles = roles,
            Permissions = await GetPermissionsAsync(roles)
        };
    }

    public virtual async Task<bool> LogoutAsync(string? sessionId)
    {
        if (!SessionToken.IsWellFormed(sessionId))
        {
            return false;
        }

        var now = Clock.Now;

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);

        var session = await _resolver.FindAsync(sessionId!);
        if (session is null || !session.IsActiveAt(now))
        {
            return false;   // không hoàn tất UoW: không ghi gì
        }

        var user = await _userManager.FindByIdAsync(session.UserId.ToString());

        session.Revoke(now);
        await _sessions.UpdateAsync(session, autoSave: true);
        await _events.WriteAsync(SecurityEventActions.Logout, user?.Email, session.UserId, reason: null);
        await uow.CompleteAsync();

        return true;
    }
    private async Task<LoginResultDto?> LoginCoreAsync(string email, string normalized, string password)
    {
        var now = Clock.Now;

        var history = await LoadHistoryAsync(normalized, now);
        var before = LoginAttemptPolicy.Evaluate(history, now);

        var user = await _userManager.FindByEmailAsync(email);

        string? failure = null;
        if (user is null)
        {
            failure = LoginFailureReasons.UnknownUser;
        }
        else if (before.IsLocked)
        {
            failure = LoginFailureReasons.Locked;
        }

        bool passwordOk;
        if (failure is null)
        {
            passwordOk = await _userManager.CheckPasswordAsync(user!, password);
            if (!passwordOk)
            {
                failure = LoginFailureReasons.InvalidPassword;
            }
            else if (!user!.IsActive)
            {
                failure = LoginFailureReasons.Inactive;
            }
        }
        else
        {
            // Không có hash thật để kiểm (hoặc đang khóa): vẫn tốn cùng thời gian băm.
            _passwordHasher.VerifyHashedPassword(null!, DummyHash.Value, password);
        }

        return failure is null
            ? await CompleteSuccessAsync(user!, normalized, now)
            : await CompleteFailureAsync(user, email, normalized, now, history, before, failure);
    }

    private async Task<List<LoginAttemptEntry>> LoadHistoryAsync(string normalized, DateTime now)
    {
        var since = now - HistoryWindow;
        var query = (await _attempts.GetQueryableAsync())
            .Where(a => a.NormalizedEmail == normalized && a.AttemptedAt >= since);
        var rows = await AsyncExecuter.ToListAsync(query);
        return rows.Select(r => r.ToEntry()).ToList();
    }

    private async Task<LoginResultDto?> CompleteSuccessAsync(AbpIdentityUser user, string normalized, DateTime now)
    {
        var token = SessionToken.Generate();
        var roles = (await _userManager.GetRolesAsync(user)).OrderBy(r => r, StringComparer.Ordinal).ToList();
        var permissions = await GetPermissionsAsync(roles);

        using (var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            await _attempts.InsertAsync(
                new LoginAttempt(GuidGenerator.Create(), normalized, user.Id, true, now), autoSave: true);
            await _sessions.InsertAsync(
                new AdminSession(GuidGenerator.Create(), SessionToken.Hash(token), user.Id, now), autoSave: true);
            await _events.WriteAsync(SecurityEventActions.LoginSucceeded, user.Email, user.Id, reason: null);
            await uow.CompleteAsync();
        }

        return new LoginResultDto
        {
            SessionId = token,
            UserId = user.Id,
            Email = user.Email,
            Roles = roles,
            Permissions = permissions
        };
    }

    private async Task<LoginResultDto?> CompleteFailureAsync(
        AbpIdentityUser? user,
        string email,
        string normalized,
        DateTime now,
        List<LoginAttemptEntry> history,
        LoginDecision before,
        string reason)
    {
        // Lần này (kể cả lần bị từ chối vì đang khóa) ghi succeeded=false; policy tự bỏ qua các lần trong thời gian khóa.
        var entry = new LoginAttemptEntry(now, false);
        var after = LoginAttemptPolicy.Evaluate(history.Append(entry), now);
        var causedLock = !before.IsLocked && after.IsLocked;

        using (var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            await _attempts.InsertAsync(
                new LoginAttempt(GuidGenerator.Create(), normalized, user?.Id, false, now), autoSave: true);
            await _events.WriteAsync(SecurityEventActions.LoginFailed, email, user?.Id, reason);
            if (causedLock)
            {
                await _events.WriteAsync(SecurityEventActions.AccountLockedOut, email, user?.Id, reason: null);
            }

            await uow.CompleteAsync();
        }

        return null;
    }

    private async Task<List<string>> GetPermissionsAsync(IEnumerable<string> roles)
    {
        var granted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            var all = await _permissionManager.GetAllAsync(RolePermissionValueProvider.ProviderName, role);
            foreach (var permission in all.Where(p => p.IsGranted))
            {
                granted.Add(permission.Name);
            }
        }

        return granted.OrderBy(p => p, StringComparer.Ordinal).ToList();
    }
}
