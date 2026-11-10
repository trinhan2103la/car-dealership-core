using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using CarDealership.CoreService.Audit;
using Volo.Abp.DependencyInjection;
using Volo.Abp.SecurityLog;

namespace CarDealership.CoreService.Identity;

public static class SecurityEventActions
{
    public const string LoginSucceeded = "AdminLoginSucceeded";
    public const string LoginFailed = "AdminLoginFailed";
    public const string AccountLockedOut = "AdminAccountLockedOut";
    public const string Logout = "AdminLogout";
    public const string SessionsRevoked = "AdminSessionsRevoked";

    /// <summary>Refresh token đã bị thay được dùng lại ngoài grace → cả family bị thu hồi (nghi đánh cắp).</summary>
    public const string RefreshTokenReuse = "AdminRefreshTokenReuse";
}

public static class LoginFailureReasons
{
    public const string UnknownUser = "UnknownUser";
    public const string InvalidPassword = "InvalidPassword";
    public const string Inactive = "Inactive";
    public const string Locked = "Locked";
}

/// <summary>Ghi sự kiện bảo mật vào ABP security log.</summary>
public interface ISecurityEventWriter
{
    Task WriteAsync(string action, string? email, Guid? userId, string? reason);
}

public class SecurityEventWriter : ISecurityEventWriter, ITransientDependency
{
    private const string InvalidEmailPlaceholder = "(invalid-email)";
    private readonly ISecurityLogManager _securityLogManager;
    private readonly IAuditTrail _auditTrail;

    public SecurityEventWriter(ISecurityLogManager securityLogManager, IAuditTrail auditTrail)
    {
        _securityLogManager = securityLogManager;
        _auditTrail = auditTrail;
    }

    public async Task WriteAsync(string action, string? email, Guid? userId, string? reason)
    {
        await _securityLogManager.SaveAsync(log =>
        {
            log.Identity = "CarDealership.Admin";
            log.Action = action;
            log.UserId = userId;
            log.UserName = SafeEmail(email);
            if (!string.IsNullOrEmpty(reason))
            {
                log.ExtraProperties["reason"] = reason;
            }

            // KHÔNG bao giờ đưa mật khẩu, session id hay hash vào đây.
        });

        // Audit_Log (Req 17.10): UoW riêng, commit ngay. Chỉ ghi mã hành động, user id và lý do (không email, không
        // mật khẩu). ABP security log ở trên vẫn giữ tên đăng nhập đã nhập để truy vết.
        await _auditTrail.WriteSecurityEventAsync(action, userId, reason);
    }

    /// <summary>
    /// Người dùng có thể gõ nhầm mật khẩu vào ô email; chỉ ghi chuỗi đúng dạng email (và đủ ngắn), còn lại thay bằng placeholder.
    /// </summary>
    internal static string SafeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return InvalidEmailPlaceholder;
        }

        var trimmed = email.Trim();
        return trimmed.Length <= 256 && new EmailAddressAttribute().IsValid(trimmed)
            ? trimmed
            : InvalidEmailPlaceholder;
    }
}
