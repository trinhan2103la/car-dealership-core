using System;
using System.Collections.Generic;
using System.Linq;

namespace CarDealership.CoreService.Identity.Login;

/// <summary>Một lần đăng nhập đã xảy ra (thời điểm UTC, đúng/sai mật khẩu). Giá trị thuần, không dính DB.</summary>
public readonly record struct LoginAttemptEntry(DateTime AttemptedAt, bool Succeeded);

/// <summary>Kết quả đánh giá tại một thời điểm.</summary>
/// <param name="IsLocked">Tài khoản đang bị khóa: MỌI lần đăng nhập, kể cả đúng mật khẩu, phải bị từ chối.</param>
/// <param name="LockedUntil">Thời điểm hết khóa (UTC) khi <paramref name="IsLocked"/>; ngược lại null.</param>
/// <param name="ConsecutiveFailures">Số lần sai liên tiếp đang được tính (0–4), chưa gồm lần đã gây khóa.</param>
public readonly record struct LoginDecision(bool IsLocked, DateTime? LockedUntil, int ConsecutiveFailures);

/// <summary>
/// Hàm THUẦN quyết định khóa đăng nhập. Không đọc đồng hồ, DB hay ASP.NET
/// Identity lockout (chỉ một nguồn sự thật); thời gian đi vào qua tham số <c>now</c> để test được.
///
/// Quy tắc, phát lại lịch sử theo thứ tự thời gian:
/// - Sai 5 lần liên tiếp mà lần thứ 5 cách lần thứ 1 của năm lần đó KHÔNG quá 15 phút (tính cả đúng 15:00)
///   thì khóa đến (lần sai thứ 5 + 15 phút); đúng tại thời điểm hết khóa là mở khóa.
/// - Cửa sổ trượt: chỉ xét 5 lần sai gần nhất liền nhau, nên sai rải rác chậm không tích lũy vô hạn
///   nhưng kẻ chờ qua cửa sổ cũng không thoát được chuỗi sai nhanh về sau.
/// - Đăng nhập đúng khi không bị khóa đặt lại bộ đếm.
/// - Lần đăng nhập xảy ra TRONG thời gian khóa bị bỏ qua hoàn toàn: không gia hạn khóa, không đặt lại bộ đếm
///   (nên đúng mật khẩu lúc đang khóa không mở khóa được). Hết khóa thì bắt đầu đếm từ 0.
/// </summary>
public static class LoginAttemptPolicy
{
    public const int MaxConsecutiveFailures = 5;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);

    public static LoginDecision Evaluate(IEnumerable<LoginAttemptEntry> history, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(history);
        EnsureUtc(now, nameof(now));

        var ordered = history.OrderBy(a => a.AttemptedAt).ToList();
        var failures = new List<DateTime>();
        DateTime? lockedUntil = null;

        foreach (var attempt in ordered)
        {
            EnsureUtc(attempt.AttemptedAt, nameof(history));
            if (attempt.AttemptedAt > now)
            {
                throw new ArgumentException("Lịch sử có lần đăng nhập sau thời điểm đánh giá.", nameof(history));
            }

            if (lockedUntil.HasValue)
            {
                if (attempt.AttemptedAt < lockedUntil.Value)
                {
                    continue;   // đang khóa: bỏ qua
                }

                lockedUntil = null;   // đã hết khóa: đếm lại từ 0
                failures.Clear();
            }

            if (attempt.Succeeded)
            {
                failures.Clear();
                continue;
            }

            failures.Add(attempt.AttemptedAt);
            // Cửa sổ trượt: bỏ các lần sai cũ hơn 15 phút so với lần sai mới nhất. Còn lại luôn nằm trong cửa sổ,
            // và vì dọn sạch sau mỗi lần khóa nên không bao giờ vượt quá 5 phần tử.
            while (failures.Count > 0 && failures[^1] - failures[0] > FailureWindow)
            {
                failures.RemoveAt(0);
            }

            if (failures.Count == MaxConsecutiveFailures)
            {
                lockedUntil = failures[^1] + LockDuration;
                failures.Clear();
            }
        }

        if (lockedUntil.HasValue && now < lockedUntil.Value)
        {
            return new LoginDecision(true, lockedUntil, 0);
        }

        // Khóa đã hết hạn tại `now`: bộ đếm về 0.
        return new LoginDecision(false, null, lockedUntil.HasValue ? 0 : failures.Count);
    }

    private static void EnsureUtc(DateTime value, string name)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Thời điểm phải là UTC.", name);
        }
    }
}
