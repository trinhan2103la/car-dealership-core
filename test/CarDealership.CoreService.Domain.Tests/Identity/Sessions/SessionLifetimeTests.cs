using System;
using CarDealership.CoreService.Identity.Sessions;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Identity.Sessions;

/// <summary>Ngày 34 (Req 14.7, 14.8): hết hạn sau 30 phút không hoạt động, thu hồi, định dạng session id.</summary>
public class SessionLifetimeTests
{
    private static readonly DateTime T0 = new(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);

    private static AdminSession NewSession() =>
        new(Guid.NewGuid(), SessionToken.Hash(SessionToken.Generate()), Guid.NewGuid(), T0);

    [Theory]
    [InlineData(0, true)]
    [InlineData(29 * 60 + 59, true)]     // 29:59 còn hạn
    [InlineData(30 * 60, false)]         // đúng 30:00 hết hạn
    [InlineData(30 * 60 + 1, false)]     // 30:01 hết hạn
    [InlineData(24 * 3600, false)]
    public void Idle_boundary_is_exclusive_at_30_minutes(int secondsSinceLastSeen, bool expectedActive)
    {
        NewSession().IsActiveAt(T0.AddSeconds(secondsSinceLastSeen)).ShouldBe(expectedActive);
    }

    [Fact]
    public void Touch_slides_the_window_so_activity_keeps_the_session_alive()
    {
        var session = NewSession();

        session.Touch(T0.AddMinutes(20));
        session.Touch(T0.AddMinutes(40));   // 40 phút kể từ lúc tạo, nhưng mỗi lần cách nhau 20 phút

        session.LastSeenAt.ShouldBe(T0.AddMinutes(40));
        session.IsActiveAt(T0.AddMinutes(69).AddSeconds(59)).ShouldBeTrue();
        session.IsActiveAt(T0.AddMinutes(70)).ShouldBeFalse();
    }

    [Fact]
    public void Touch_never_moves_last_seen_backwards()
    {
        var session = NewSession();
        session.Touch(T0.AddMinutes(10));

        session.Touch(T0.AddMinutes(5));   // request song song với đồng hồ lệch

        session.LastSeenAt.ShouldBe(T0.AddMinutes(10));
    }

    [Fact]
    public void Touch_on_an_expired_session_is_refused()
    {
        Should.Throw<InvalidOperationException>(() => NewSession().Touch(T0.AddMinutes(31)));
    }

    [Fact]
    public void Touch_on_a_revoked_session_is_refused()
    {
        var session = NewSession();
        session.Revoke(T0.AddMinutes(1));

        Should.Throw<InvalidOperationException>(() => session.Touch(T0.AddMinutes(2)));
    }

    [Fact]
    public void Revoke_makes_the_session_unusable_and_keeps_the_first_revocation_time()
    {
        var session = NewSession();

        session.Revoke(T0.AddMinutes(5));
        session.Revoke(T0.AddMinutes(9));

        session.IsRevoked.ShouldBeTrue();
        session.RevokedAt.ShouldBe(T0.AddMinutes(5));
        session.IsActiveAt(T0.AddMinutes(6)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Non_utc_times_are_rejected(DateTimeKind kind)
    {
        var session = NewSession();
        var bad = new DateTime(2026, 11, 3, 8, 0, 0, kind);

        Should.Throw<ArgumentException>(() => session.IsActiveAt(bad));
        Should.Throw<ArgumentException>(() => session.Touch(bad));
        Should.Throw<ArgumentException>(() => session.Revoke(bad));
    }

    [Fact]
    public void Generated_tokens_are_well_formed()
    {
        for (var i = 0; i < 200; i++)
        {
            SessionToken.IsWellFormed(SessionToken.Generate()).ShouldBeTrue();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public void Null_empty_or_short_tokens_are_not_well_formed(string? token)
    {
        SessionToken.IsWellFormed(token).ShouldBeFalse();
    }
    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]    // dấu + (base64 chuẩn, không phải base64url)
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]    // khoảng trắng cuối
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]   // 44 ký tự
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]     // 42 ký tự
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n")]
    [InlineData("'; DROP TABLE idn_admin_sessions; --AAAAAAAAAA")]
    public void Characters_outside_base64url_or_wrong_length_are_rejected(string token)
    {
        SessionToken.IsWellFormed(token).ShouldBeFalse();
    }
}
