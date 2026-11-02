using System;
using Volo.Abp.Domain.Entities;

namespace CarDealership.CoreService.Identity.Sessions;

/// <summary>
/// ADR-005, bảng <c>idn_admin_sessions</c>: phiên đăng nhập Admin_User. Chỉ lưu SHA-256 của session id,
/// KHÔNG có chỗ nào để lưu giá trị gốc. <see cref="UserId"/> không có FK vì khác module với AbpUsers.
/// Mọi thời điểm là UTC.
/// </summary>
public class AdminSession : Entity<Guid>
{
    public byte[] TokenHash { get; private set; } = Array.Empty<byte>();
    public Guid UserId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime LastSeenAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    public bool IsRevoked => RevokedAt.HasValue;

    /// <summary>Req 14.7: còn dùng được tại <paramref name="nowUtc"/> khi chưa bị thu hồi và chưa quá hạn không hoạt động.</summary>
    public bool IsActiveAt(DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        return !IsRevoked && SessionLifetime.IsWithinIdleTimeout(LastSeenAt, nowUtc);
    }

    /// <summary>Ghi nhận một request đã xác thực (trượt cửa sổ idle). Chỉ gọi khi <see cref="IsActiveAt"/> đúng.</summary>
    public void Touch(DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        if (!IsActiveAt(nowUtc))
        {
            throw new InvalidOperationException("Không thể gia hạn session đã hết hạn hoặc bị thu hồi.");
        }

        // Không bao giờ lùi lại (đồng hồ lệch giữa hai request song song).
        if (nowUtc > LastSeenAt)
        {
            LastSeenAt = nowUtc;
        }
    }

    /// <summary>Thu hồi (đăng xuất / user Inactive). Idempotent: giữ nguyên thời điểm thu hồi đầu tiên.</summary>
    public void Revoke(DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        RevokedAt ??= nowUtc;
    }

    private AdminSession()
    {
        // EF Core
    }

    public AdminSession(Guid id, byte[] tokenHash, Guid userId, DateTime nowUtc) : base(id)
    {
        if (tokenHash is null || tokenHash.Length != SessionToken.HashBytes)
        {
            throw new ArgumentException($"Token hash phải dài đúng {SessionToken.HashBytes} byte (SHA-256).", nameof(tokenHash));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId không được rỗng.", nameof(userId));
        }

        EnsureUtc(nowUtc);

        TokenHash = tokenHash;
        UserId = userId;
        CreatedAt = nowUtc;
        LastSeenAt = nowUtc;
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Thời điểm phải là UTC.", nameof(value));
        }
    }
}
