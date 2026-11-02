using System;

namespace CarDealership.CoreService.Identity.Sessions;

/// <summary>
/// Req 14.7: phiên hết hạn khi không có request đã xác thực nào trong 30 phút. Hàm thuần để test biên.
/// Quy ước biên: còn hạn khi (now − lastSeen) &lt; 30 phút; ĐÚNG 30:00 là hết hạn.
/// </summary>
public static class SessionLifetime
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    public static bool IsWithinIdleTimeout(DateTime lastSeenUtc, DateTime nowUtc) =>
        nowUtc - lastSeenUtc < IdleTimeout;
}
