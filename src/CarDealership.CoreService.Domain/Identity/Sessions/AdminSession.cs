using System;
using System.Security.Cryptography;
using Volo.Abp.Domain.Entities;

namespace CarDealership.CoreService.Identity.Sessions;

/// <summary>Kết quả so một refresh token với family.</summary>
public enum RefreshTokenMatch
{
    /// <summary>Không thuộc family này.</summary>
    None,

    /// <summary>Đúng token hiện hành: xoay vòng.</summary>
    Current,

    /// <summary>Token vừa bị thay, còn trong grace: chấp nhận nhưng KHÔNG xoay.</summary>
    PreviousWithinGrace,

    /// <summary>Token đã bị thay, quá grace: DÙNG LẠI, nghi bị đánh cắp → thu hồi cả family.</summary>
    Reused,
}

/// <summary>
/// Bảng <c>idn_admin_sessions</c>: MỘT phiên đăng nhập của Admin_User = một family refresh token.
/// <see cref="Entity{TKey}.Id"/> là mã family, đi trong claim <c>sid</c> của access token (BFF ký) và Service_Credential.
/// Chỉ lưu SHA-256 của refresh token hiện hành và của token ngay trước đó (để nhận ra grace / dùng lại).
/// <see cref="UserId"/> không có FK vì khác module với AbpUsers. Mọi thời điểm là UTC.
/// </summary>
public class AdminSession : Entity<Guid>
{
    public const int MaxRevokeReasonLength = 64;

    public byte[] TokenHash { get; private set; } = Array.Empty<byte>();
    public byte[]? PreviousTokenHash { get; private set; }
    public DateTime? RotatedAt { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>Lần đăng nhập/refresh gần nhất (mốc tính idle 3 ngày).</summary>
    public DateTime LastSeenAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }
    public string? RevokeReason { get; private set; }

    public bool IsRevoked => RevokedAt.HasValue;

    public DateTime AbsoluteExpiresAt => CreatedAt + SessionLifetime.AbsoluteLifetime;

    /// <summary>Còn dùng được: chưa thu hồi, chưa quá idle 3 ngày, chưa quá 7 ngày kể từ đăng nhập.</summary>
    public bool IsActiveAt(DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        return !IsRevoked
               && SessionLifetime.IsWithinIdleTimeout(LastSeenAt, nowUtc)
               && SessionLifetime.IsWithinAbsoluteLifetime(CreatedAt, nowUtc);
    }

    /// <summary>So hash của refresh token được trình với family (thời gian hằng).</summary>
    public RefreshTokenMatch Match(byte[] presentedHash, DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        if (presentedHash is null || presentedHash.Length != SessionToken.HashBytes)
        {
            return RefreshTokenMatch.None;
        }

        if (CryptographicOperations.FixedTimeEquals(presentedHash, TokenHash))
        {
            return RefreshTokenMatch.Current;
        }

        if (PreviousTokenHash is not null && CryptographicOperations.FixedTimeEquals(presentedHash, PreviousTokenHash))
        {
            return RotatedAt.HasValue && SessionLifetime.IsWithinReuseGrace(RotatedAt.Value, nowUtc)
                ? RefreshTokenMatch.PreviousWithinGrace
                : RefreshTokenMatch.Reused;
        }

        return RefreshTokenMatch.None;
    }

    /// <summary>Xoay vòng: token hiện hành thành "previous", <paramref name="newTokenHash"/> thành hiện hành; trượt idle.</summary>
    public void Rotate(byte[] newTokenHash, DateTime nowUtc)
    {
        EnsureHash(newTokenHash, nameof(newTokenHash));
        if (!IsActiveAt(nowUtc))
        {
            throw new InvalidOperationException("Không thể xoay refresh token của phiên đã hết hạn hoặc bị thu hồi.");
        }

        PreviousTokenHash = TokenHash;
        TokenHash = newTokenHash;
        RotatedAt = nowUtc;
        Touch(nowUtc);
    }

    /// <summary>Ghi nhận một lần dùng hợp lệ (trượt cửa sổ idle). Chỉ gọi khi <see cref="IsActiveAt"/> đúng.</summary>
    public void Touch(DateTime nowUtc)
    {
        EnsureUtc(nowUtc);
        if (!IsActiveAt(nowUtc))
        {
            throw new InvalidOperationException("Không thể gia hạn phiên đã hết hạn hoặc bị thu hồi.");
        }

        // Không bao giờ lùi lại (đồng hồ lệch giữa hai request song song).
        if (nowUtc > LastSeenAt)
        {
            LastSeenAt = nowUtc;
        }
    }

    /// <summary>Thu hồi (đăng xuất / dùng lại token / user Inactive). Idempotent: giữ thời điểm và lý do đầu tiên.</summary>
    public void Revoke(DateTime nowUtc, string? reason = null)
    {
        EnsureUtc(nowUtc);
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = nowUtc;
        RevokeReason = reason is { Length: > MaxRevokeReasonLength } ? reason[..MaxRevokeReasonLength] : reason;
    }

    private AdminSession()
    {
        // EF Core
    }

    public AdminSession(Guid id, byte[] tokenHash, Guid userId, DateTime nowUtc) : base(id)
    {
        EnsureHash(tokenHash, nameof(tokenHash));

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId không được rỗng.", nameof(userId));
        }

        EnsureUtc(nowUtc);

        TokenHash = tokenHash;
        UserId = userId;
        // PostgreSQL lưu timestamptz tới micro giây; .NET giữ tới 100 ns. Làm tròn ngay từ đầu để mốc hết hạn tuyệt đối
        // (CreatedAt + 7 ngày) trả lúc đăng nhập khớp đúng giá trị đọc lại từ DB lúc refresh.
        CreatedAt = TruncateToMicroseconds(nowUtc);
        LastSeenAt = CreatedAt;
    }

    private static DateTime TruncateToMicroseconds(DateTime value) =>
        new(value.Ticks - value.Ticks % 10, DateTimeKind.Utc);

    private static void EnsureHash(byte[]? hash, string name)
    {
        if (hash is null || hash.Length != SessionToken.HashBytes)
        {
            throw new ArgumentException($"Token hash phải dài đúng {SessionToken.HashBytes} byte (SHA-256).", name);
        }
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Thời điểm phải là UTC.", nameof(value));
        }
    }
}
