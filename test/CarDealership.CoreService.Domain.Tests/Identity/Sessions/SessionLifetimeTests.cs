using System;
using CarDealership.CoreService.Identity.Sessions;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Identity.Sessions;

/// <summary>Idle 3 ngày, tuyệt đối 7 ngày, xoay vòng + grace 20 s + phát hiện dùng lại, thu hồi.</summary>
public class SessionLifetimeTests
{
    private static readonly DateTime T0 = new(2026, 11, 3, 8, 0, 0, DateTimeKind.Utc);

    private static AdminSession NewSession(out string token)
    {
        token = SessionToken.Generate();
        return new AdminSession(Guid.NewGuid(), SessionToken.Hash(token), Guid.NewGuid(), T0);
    }

    private static AdminSession NewSession() => NewSession(out _);

    // ---------- idle 3 ngày ----------

    [Theory]
    [InlineData(0, true)]
    [InlineData(3 * 86400 - 1, true)]     // 2 ngày 23:59:59 còn hạn
    [InlineData(3 * 86400, false)]        // đúng 3 ngày hết hạn
    [InlineData(3 * 86400 + 1, false)]
    public void Idle_boundary_is_exclusive_at_3_days(int secondsSinceLastSeen, bool expectedActive)
    {
        NewSession().IsActiveAt(T0.AddSeconds(secondsSinceLastSeen)).ShouldBe(expectedActive);
    }

    [Fact]
    public void Lock_screen_overnight_does_not_expire_the_session()
    {
        NewSession().IsActiveAt(T0.AddHours(16)).ShouldBeTrue();
    }

    // ---------- tuyệt đối 7 ngày ----------

    [Fact]
    public void Continuous_use_still_expires_exactly_7_days_after_login()
    {
        var session = NewSession();
        for (var hours = 10; hours < 7 * 24; hours += 10)
        {
            session.Touch(T0.AddHours(hours));   // dùng đều mỗi 10 giờ: idle không bao giờ hết
        }

        session.IsActiveAt(T0.AddDays(7).AddSeconds(-1)).ShouldBeTrue();
        session.IsActiveAt(T0.AddDays(7)).ShouldBeFalse();
        session.AbsoluteExpiresAt.ShouldBe(T0.AddDays(7));
    }

    // ---------- xoay vòng ----------

    [Fact]
    public void Rotate_makes_the_new_token_current_and_the_old_one_previous()
    {
        var session = NewSession(out var first);
        var second = SessionToken.Generate();
        var at = T0.AddMinutes(10);

        session.Rotate(SessionToken.Hash(second), at);

        session.Match(SessionToken.Hash(second), at).ShouldBe(RefreshTokenMatch.Current);
        session.Match(SessionToken.Hash(first), at).ShouldBe(RefreshTokenMatch.PreviousWithinGrace);
        session.RotatedAt.ShouldBe(at);
        session.LastSeenAt.ShouldBe(at);
    }

    [Theory]
    [InlineData(0, RefreshTokenMatch.PreviousWithinGrace)]
    [InlineData(19, RefreshTokenMatch.PreviousWithinGrace)]
    [InlineData(20, RefreshTokenMatch.Reused)]       // đúng 20 s: hết grace
    [InlineData(3600, RefreshTokenMatch.Reused)]
    public void Previous_token_is_accepted_only_inside_the_20_second_grace(int secondsAfterRotation, RefreshTokenMatch expected)
    {
        var session = NewSession(out var first);
        var rotatedAt = T0.AddMinutes(10);
        session.Rotate(SessionToken.Hash(SessionToken.Generate()), rotatedAt);

        session.Match(SessionToken.Hash(first), rotatedAt.AddSeconds(secondsAfterRotation)).ShouldBe(expected);
    }

    [Fact]
    public void A_token_two_rotations_old_is_unknown_to_the_family()
    {
        var session = NewSession(out var first);
        session.Rotate(SessionToken.Hash(SessionToken.Generate()), T0.AddMinutes(10));
        session.Rotate(SessionToken.Hash(SessionToken.Generate()), T0.AddMinutes(20));

        session.Match(SessionToken.Hash(first), T0.AddMinutes(20)).ShouldBe(RefreshTokenMatch.None);
    }

    [Fact]
    public void Unknown_or_malformed_hashes_do_not_match()
    {
        var session = NewSession();

        session.Match(SessionToken.Hash(SessionToken.Generate()), T0).ShouldBe(RefreshTokenMatch.None);
        session.Match(new byte[5], T0).ShouldBe(RefreshTokenMatch.None);
        session.Match(null!, T0).ShouldBe(RefreshTokenMatch.None);
    }

    [Fact]
    public void Rotation_is_refused_on_an_expired_or_revoked_session()
    {
        var expired = NewSession();
        Should.Throw<InvalidOperationException>(() => expired.Rotate(new byte[32], T0.AddDays(4)));

        var revoked = NewSession();
        revoked.Revoke(T0.AddMinutes(1), "Logout");
        Should.Throw<InvalidOperationException>(() => revoked.Rotate(new byte[32], T0.AddMinutes(2)));
    }

    [Fact]
    public void Rotation_with_a_bad_hash_is_rejected()
    {
        Should.Throw<ArgumentException>(() => NewSession().Rotate(new byte[31], T0.AddMinutes(1)));
    }

    // ---------- Touch ----------

    [Fact]
    public void Touch_never_moves_last_seen_backwards()
    {
        var session = NewSession();
        session.Touch(T0.AddMinutes(10));

        session.Touch(T0.AddMinutes(5));

        session.LastSeenAt.ShouldBe(T0.AddMinutes(10));
    }

    [Fact]
    public void Touch_on_an_expired_or_revoked_session_is_refused()
    {
        Should.Throw<InvalidOperationException>(() => NewSession().Touch(T0.AddDays(3)));

        var session = NewSession();
        session.Revoke(T0.AddMinutes(1));
        Should.Throw<InvalidOperationException>(() => session.Touch(T0.AddMinutes(2)));
    }

    // ---------- thu hồi ----------

    [Fact]
    public void Revoke_keeps_the_first_time_and_reason()
    {
        var session = NewSession();

        session.Revoke(T0.AddMinutes(5), "RefreshTokenReuse");
        session.Revoke(T0.AddMinutes(9), "Logout");

        session.IsRevoked.ShouldBeTrue();
        session.RevokedAt.ShouldBe(T0.AddMinutes(5));
        session.RevokeReason.ShouldBe("RefreshTokenReuse");
        session.IsActiveAt(T0.AddMinutes(6)).ShouldBeFalse();
    }

    [Fact]
    public void Overlong_revoke_reason_is_truncated()
    {
        var session = NewSession();
        session.Revoke(T0, new string('x', 200));
        session.RevokeReason!.Length.ShouldBe(AdminSession.MaxRevokeReasonLength);
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
        Should.Throw<ArgumentException>(() => session.Match(new byte[32], bad));
    }

    // ---------- định dạng token ----------

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
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA ")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\n")]
    [InlineData("'; DROP TABLE idn_admin_sessions; --AAAAAAAAAA")]
    public void Malformed_tokens_are_rejected(string? token)
    {
        SessionToken.IsWellFormed(token).ShouldBeFalse();
    }
}
