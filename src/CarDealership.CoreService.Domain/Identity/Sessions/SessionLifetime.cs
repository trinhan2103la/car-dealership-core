using System;

namespace CarDealership.CoreService.Identity.Sessions;

/// <summary>
/// Thời hạn của MỘT phiên đăng nhập (= một "family" refresh token). Hàm thuần để test biên.
/// - Access token (BFF ký) sống 10 phút; không nằm ở đây vì Core_API không phát access token.
/// - Idle: phiên hết hạn khi không có lần refresh/đăng nhập nào trong 3 ngày (SPA refresh ~10 phút/lần khi đang mở).
/// - Tuyệt đối: dù dùng liên tục, phiên hết hạn sau 7 ngày kể từ lúc đăng nhập.
/// - Grace: refresh token VỪA bị thay (trong 20 giây) vẫn được chấp nhận một cách "không xoay", để hai tab/hai request
///   refresh gần như cùng lúc không bị coi là đánh cắp.
/// Quy ước biên: còn hạn khi khoảng cách &lt; giới hạn; ĐÚNG bằng giới hạn là hết hạn.
/// </summary>
public static class SessionLifetime
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromDays(3);
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(7);
    public static readonly TimeSpan ReuseGrace = TimeSpan.FromSeconds(20);

    public static bool IsWithinIdleTimeout(DateTime lastSeenUtc, DateTime nowUtc) =>
        nowUtc - lastSeenUtc < IdleTimeout;

    public static bool IsWithinAbsoluteLifetime(DateTime createdUtc, DateTime nowUtc) =>
        nowUtc - createdUtc < AbsoluteLifetime;

    public static bool IsWithinReuseGrace(DateTime rotatedUtc, DateTime nowUtc) =>
        nowUtc - rotatedUtc < ReuseGrace;
}
