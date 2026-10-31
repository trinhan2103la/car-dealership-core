using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CarDealership.CoreService.Identity.Sessions;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Identity.Sessions;

/// <summary>Ngày 30 (ADR-005, Req 14.7): session id 256 bit từ CSPRNG; chỉ giữ SHA-256; entity không nhận giá trị gốc.</summary>
public class AdminSessionTests
{
    private static readonly DateTime Now = new(2026, 10, 31, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Generated_token_carries_256_bits_in_43_base64url_characters()
    {
        var token = SessionToken.Generate();

        token.Length.ShouldBe(43);
        token.ShouldMatch("^[A-Za-z0-9_-]{43}$");

        var padded = token.Replace('-', '+').Replace('_', '/') + "=";
        Convert.FromBase64String(padded).Length.ShouldBe(SessionToken.EntropyBytes);
    }

    [Fact]
    public void Ten_thousand_generated_tokens_are_all_distinct()
    {
        var tokens = Enumerable.Range(0, 10_000).Select(_ => SessionToken.Generate()).ToHashSet();

        tokens.Count.ShouldBe(10_000);
    }

    [Fact]
    public void Hash_is_sha256_of_the_utf8_token_and_deterministic()
    {
        var token = SessionToken.Generate();

        var hash = SessionToken.Hash(token);

        hash.Length.ShouldBe(32);
        hash.ShouldBe(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        SessionToken.Hash(token).ShouldBe(hash);
        SessionToken.Hash(SessionToken.Generate()).ShouldNotBe(hash);
    }

    [Fact]
    public void Hash_does_not_contain_the_raw_token_bytes()
    {
        var token = SessionToken.Generate();
        var raw = Encoding.UTF8.GetBytes(token);

        var hash = SessionToken.Hash(token);

        hash.ShouldNotBe(raw);
        Encoding.UTF8.GetString(hash).ShouldNotContain(token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Hashing_an_empty_token_is_rejected(string? token)
    {
        Should.Throw<ArgumentException>(() => SessionToken.Hash(token!));
    }

    [Fact]
    public void New_session_records_hash_user_and_utc_times_and_is_not_revoked()
    {
        var hash = SessionToken.Hash(SessionToken.Generate());
        var userId = Guid.NewGuid();

        var session = new AdminSession(Guid.NewGuid(), hash, userId, Now);

        session.TokenHash.ShouldBe(hash);
        session.UserId.ShouldBe(userId);
        session.CreatedAt.ShouldBe(Now);
        session.LastSeenAt.ShouldBe(Now);
        session.RevokedAt.ShouldBeNull();
        session.IsRevoked.ShouldBeFalse();
    }

    public static IEnumerable<object?[]> InvalidHashes => new[]
    {
        new object?[] { null },
        new object?[] { Array.Empty<byte>() },
        new object?[] { new byte[31] },
        new object?[] { new byte[33] },
        new object?[] { new byte[64] },
    };

    [Theory]
    [MemberData(nameof(InvalidHashes))]
    public void Hash_of_any_length_other_than_32_bytes_is_rejected(byte[]? hash)
    {
        Should.Throw<ArgumentException>(() => new AdminSession(Guid.NewGuid(), hash!, Guid.NewGuid(), Now));
    }

    [Fact]
    public void Empty_user_id_is_rejected()
    {
        Should.Throw<ArgumentException>(() =>
            new AdminSession(Guid.NewGuid(), new byte[32], Guid.Empty, Now));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Non_utc_time_is_rejected(DateTimeKind kind)
    {
        var notUtc = new DateTime(2026, 10, 31, 4, 0, 0, kind);

        Should.Throw<ArgumentException>(() =>
            new AdminSession(Guid.NewGuid(), new byte[32], Guid.NewGuid(), notUtc));
    }
}
