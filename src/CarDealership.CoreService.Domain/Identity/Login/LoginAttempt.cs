using System;
using Volo.Abp.Domain.Entities;

namespace CarDealership.CoreService.Identity.Login;

/// <summary>
/// Bảng <c>idn_login_attempts</c>: mỗi lần đăng nhập (đúng/sai) theo email đã chuẩn hóa. Email không tồn tại
/// vẫn được ghi (<see cref="UserId"/> = null) để chống dò tài khoản (hai trường hợp không phân biệt được).
/// Chỉ thêm, không sửa.
/// </summary>
public class LoginAttempt : Entity<Guid>
{
    public const int MaxEmailLength = 320;

    public string NormalizedEmail { get; private set; } = string.Empty;
    public Guid? UserId { get; private set; }
    public bool Succeeded { get; private set; }
    public DateTime AttemptedAt { get; private set; }

    private LoginAttempt()
    {
        // EF Core
    }

    public LoginAttempt(Guid id, string normalizedEmail, Guid? userId, bool succeeded, DateTime attemptedAtUtc) : base(id)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail) || normalizedEmail.Length > MaxEmailLength)
        {
            throw new ArgumentException($"Email chuẩn hóa phải dài 1–{MaxEmailLength} ký tự.", nameof(normalizedEmail));
        }

        if (normalizedEmail != LoginEmail.Normalize(normalizedEmail))
        {
            throw new ArgumentException("Email chưa được chuẩn hóa bằng LoginEmail.Normalize.", nameof(normalizedEmail));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId không được là Guid rỗng (dùng null khi email không tồn tại).", nameof(userId));
        }

        if (attemptedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Thời điểm phải là UTC.", nameof(attemptedAtUtc));
        }

        NormalizedEmail = normalizedEmail;
        UserId = userId;
        Succeeded = succeeded;
        AttemptedAt = attemptedAtUtc;
    }

    public LoginAttemptEntry ToEntry() => new(AttemptedAt, Succeeded);
}
