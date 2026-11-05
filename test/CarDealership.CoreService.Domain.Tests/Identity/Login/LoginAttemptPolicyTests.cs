using System;
using System.Collections.Generic;
using System.Linq;
using CarDealership.CoreService.Identity.Login;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Identity.Login;

/// <summary>
/// Bảng ca biên cho LoginAttemptPolicy. Thời gian đi vào qua tham số,
/// không có đồng hồ thật nên mọi ca xác định.
/// </summary>
public class LoginAttemptPolicyTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

    private static LoginAttemptEntry Fail(double minutes, double seconds = 0) =>
        new(T0.AddMinutes(minutes).AddSeconds(seconds), false);

    private static LoginAttemptEntry Ok(double minutes, double seconds = 0) =>
        new(T0.AddMinutes(minutes).AddSeconds(seconds), true);

    private static DateTime At(double minutes, double seconds = 0) => T0.AddMinutes(minutes).AddSeconds(seconds);

    [Fact]
    public void Empty_history_is_not_locked()
    {
        var decision = LoginAttemptPolicy.Evaluate(Array.Empty<LoginAttemptEntry>(), T0);

        decision.IsLocked.ShouldBeFalse();
        decision.LockedUntil.ShouldBeNull();
        decision.ConsecutiveFailures.ShouldBe(0);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Fewer_than_five_failures_never_lock(int failures)
    {
        var history = Enumerable.Range(0, failures).Select(i => Fail(i)).ToList();

        var decision = LoginAttemptPolicy.Evaluate(history, At(failures));

        decision.IsLocked.ShouldBeFalse();
        decision.ConsecutiveFailures.ShouldBe(failures);
    }

    [Fact]
    public void Five_failures_in_a_row_lock_for_15_minutes_from_the_fifth()
    {
        var history = new[] { Fail(0), Fail(1), Fail(2), Fail(3), Fail(4) };

        var decision = LoginAttemptPolicy.Evaluate(history, At(4));

        decision.IsLocked.ShouldBeTrue();
        decision.LockedUntil.ShouldBe(At(19));
        decision.ConsecutiveFailures.ShouldBe(0);
    }

    [Fact]
    public void Fifth_failure_at_14_59_after_the_first_still_locks()
    {
        var history = new[] { Fail(0), Fail(1), Fail(2), Fail(3), Fail(14, 59) };

        LoginAttemptPolicy.Evaluate(history, At(14, 59)).IsLocked.ShouldBeTrue();
    }

    [Fact]
    public void Fifth_failure_at_exactly_15_00_after_the_first_locks_inclusive_boundary()
    {
        var history = new[] { Fail(0), Fail(1), Fail(2), Fail(3), Fail(15) };

        LoginAttemptPolicy.Evaluate(history, At(15)).IsLocked.ShouldBeTrue();
    }

    [Fact]
    public void Fifth_failure_at_15_01_after_the_first_does_not_lock()
    {
        var history = new[] { Fail(0), Fail(1), Fail(2), Fail(3), Fail(15, 1) };

        var decision = LoginAttemptPolicy.Evaluate(history, At(15, 1));

        decision.IsLocked.ShouldBeFalse();
        decision.ConsecutiveFailures.ShouldBe(4);   // cửa sổ trượt: còn 4 lần gần nhất trong cửa sổ, lần đầu rơi ra
    }

    [Fact]
    public void Sliding_window_a_later_fast_streak_still_locks_after_slow_failures()
    {
        // Kẻ tấn công chờ qua cửa sổ rồi thử nhanh: 0, 10, 20 phút rồi 20:30, 21:00, 21:30 -> 5 lần gần nhất trong 11,5 phút.
        var history = new[] { Fail(0), Fail(10), Fail(20), Fail(20, 30), Fail(21), Fail(21, 30) };

        LoginAttemptPolicy.Evaluate(history, At(21, 30)).IsLocked.ShouldBeTrue();
    }

    [Fact]
    public void Success_resets_the_counter()
    {
        var history = new[] { Fail(0), Fail(1), Fail(2), Fail(3), Ok(4), Fail(5), Fail(6), Fail(7), Fail(8) };

        var decision = LoginAttemptPolicy.Evaluate(history, At(8));

        decision.IsLocked.ShouldBeFalse();
        decision.ConsecutiveFailures.ShouldBe(4);
    }

    [Fact]
    public void Failures_separated_by_a_success_are_not_consecutive()
    {
        var history = new[] { Fail(0), Fail(1), Fail(2), Ok(3), Fail(4), Fail(5) };

        LoginAttemptPolicy.Evaluate(history, At(5)).IsLocked.ShouldBeFalse();
    }

    [Fact]
    public void Correct_password_while_locked_does_not_unlock_or_extend()
    {
        var history = new[] { Fail(0), Fail(1), Fail(2), Fail(3), Fail(4), Ok(10) };

        var during = LoginAttemptPolicy.Evaluate(history, At(10));
        var justBeforeEnd = LoginAttemptPolicy.Evaluate(history, At(18, 59));

        during.IsLocked.ShouldBeTrue();
        during.LockedUntil.ShouldBe(At(19));     // không bị gia hạn bởi lần đăng nhập lúc 10
        justBeforeEnd.IsLocked.ShouldBeTrue();
    }

    [Fact]
    public void Failures_while_locked_do_not_extend_the_lock()
    {
        var history = new[] { Fail(0), Fail(1), Fail(2), Fail(3), Fail(4), Fail(10), Fail(15) };

        var decision = LoginAttemptPolicy.Evaluate(history, At(15));

        decision.LockedUntil.ShouldBe(At(19));
    }

    [Fact]
    public void Lock_ends_exactly_15_minutes_after_the_fifth_failure()
    {
        var history = new[] { Fail(0), Fail(1), Fail(2), Fail(3), Fail(4) };

        LoginAttemptPolicy.Evaluate(history, At(18, 59)).IsLocked.ShouldBeTrue();
        LoginAttemptPolicy.Evaluate(history, At(19)).IsLocked.ShouldBeFalse();
        LoginAttemptPolicy.Evaluate(history, At(19, 1)).IsLocked.ShouldBeFalse();
    }

    [Fact]
    public void After_the_lock_expires_counting_starts_again_from_zero()
    {
        // Các lần sai trong lúc khóa không tính; sau 19:00 cần đủ 5 lần mới nữa.
        var history = new[]
        {
            Fail(0), Fail(1), Fail(2), Fail(3), Fail(4),
            Fail(5), Fail(6), Fail(7), Fail(8),                 // trong lúc khóa: bỏ qua
            Fail(19), Fail(20), Fail(21), Fail(22),             // 4 lần mới
        };

        var decision = LoginAttemptPolicy.Evaluate(history, At(22));

        decision.IsLocked.ShouldBeFalse();
        decision.ConsecutiveFailures.ShouldBe(4);

        var fifth = history.Append(Fail(23)).ToList();
        LoginAttemptPolicy.Evaluate(fifth, At(23)).IsLocked.ShouldBeTrue();
    }

    [Fact]
    public void Second_lock_after_the_first_expired_lasts_15_minutes_from_its_own_fifth_failure()
    {
        var history = new[]
        {
            Fail(0), Fail(1), Fail(2), Fail(3), Fail(4),
            Fail(30), Fail(31), Fail(32), Fail(33), Fail(34),
        };

        var decision = LoginAttemptPolicy.Evaluate(history, At(34));

        decision.IsLocked.ShouldBeTrue();
        decision.LockedUntil.ShouldBe(At(49));
    }

    [Fact]
    public void History_order_does_not_matter()
    {
        var ordered = new[] { Fail(0), Fail(1), Fail(2), Fail(3), Fail(4) };
        var shuffled = new[] { ordered[3], ordered[0], ordered[4], ordered[2], ordered[1] };

        LoginAttemptPolicy.Evaluate(shuffled, At(4)).ShouldBe(LoginAttemptPolicy.Evaluate(ordered, At(4)));
    }

    [Fact]
    public void A_stale_failure_alone_does_not_count_toward_a_later_lock_when_a_success_sits_between()
    {
        var history = new[] { Fail(0), Ok(1), Fail(2), Fail(3), Fail(4), Fail(5) };

        var decision = LoginAttemptPolicy.Evaluate(history, At(5));

        decision.IsLocked.ShouldBeFalse();
        decision.ConsecutiveFailures.ShouldBe(4);
    }

    [Fact]
    public void Attempt_after_the_evaluation_time_is_rejected_as_a_caller_bug()
    {
        Should.Throw<ArgumentException>(() => LoginAttemptPolicy.Evaluate(new[] { Fail(5) }, At(1)));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Non_utc_times_are_rejected(DateTimeKind kind)
    {
        var bad = new DateTime(2026, 11, 1, 9, 0, 0, kind);

        Should.Throw<ArgumentException>(() => LoginAttemptPolicy.Evaluate(Array.Empty<LoginAttemptEntry>(), bad));
        Should.Throw<ArgumentException>(() =>
            LoginAttemptPolicy.Evaluate(new[] { new LoginAttemptEntry(bad, false) }, T0.AddHours(1)));
    }

    [Fact]
    public void Evaluate_does_not_mutate_or_depend_on_the_input_collection()
    {
        var list = new List<LoginAttemptEntry> { Fail(2), Fail(1), Fail(0) };
        var copy = list.ToList();

        LoginAttemptPolicy.Evaluate(list, At(5));

        list.ShouldBe(copy);
    }

    [Fact]
    public void Constants_match_requirement_14_4()
    {
        LoginAttemptPolicy.MaxConsecutiveFailures.ShouldBe(5);
        LoginAttemptPolicy.FailureWindow.ShouldBe(TimeSpan.FromMinutes(15));
        LoginAttemptPolicy.LockDuration.ShouldBe(TimeSpan.FromMinutes(15));
    }
}

public class LoginEmailAndAttemptTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Admin@Example.COM", "ADMIN@EXAMPLE.COM")]
    [InlineData("  admin@example.com  ", "ADMIN@EXAMPLE.COM")]
    [InlineData("\tAdmin@example.com\n", "ADMIN@EXAMPLE.COM")]
    public void Email_matches_case_insensitively_after_trim(string input, string expected)
    {
        LoginEmail.Normalize(input).ShouldBe(expected);
    }

    [Fact]
    public void Attempt_with_unknown_user_keeps_null_user_id_and_converts_to_entry()
    {
        var attempt = new LoginAttempt(Guid.NewGuid(), LoginEmail.Normalize("x@y.com"), null, false, Now);

        attempt.UserId.ShouldBeNull();
        attempt.ToEntry().ShouldBe(new LoginAttemptEntry(Now, false));
    }

    [Fact]
    public void Attempt_rejects_unnormalized_empty_overlong_or_non_utc_input()
    {
        Should.Throw<ArgumentException>(() => new LoginAttempt(Guid.NewGuid(), "x@y.com", null, false, Now));
        Should.Throw<ArgumentException>(() => new LoginAttempt(Guid.NewGuid(), "  ", null, false, Now));
        Should.Throw<ArgumentException>(() =>
            new LoginAttempt(Guid.NewGuid(), new string('A', LoginAttempt.MaxEmailLength + 1), null, false, Now));
        Should.Throw<ArgumentException>(() =>
            new LoginAttempt(Guid.NewGuid(), "X@Y.COM", Guid.Empty, false, Now));
        Should.Throw<ArgumentException>(() =>
            new LoginAttempt(Guid.NewGuid(), "X@Y.COM", null, false, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }
}
