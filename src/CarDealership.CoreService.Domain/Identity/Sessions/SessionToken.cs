using System;
using System.Security.Cryptography;
using System.Text;

namespace CarDealership.CoreService.Identity.Sessions;

/// <summary>
/// ADR-005: session id mờ 256 bit. Giá trị gốc CHỈ trả cho client một lần (cookie qua BFF);
/// Core_API chỉ lưu <see cref="Hash"/> (SHA-256). Không bao giờ dùng Guid (chỉ 122 bit ngẫu nhiên).
/// </summary>
public static class SessionToken
{
    public const int EntropyBytes = 32;       // 256 bit
    public const int HashBytes = 32;          // SHA-256

    /// <summary>Sinh session id mới bằng RandomNumberGenerator (CSPRNG), mã hóa base64url không đệm (43 ký tự).</summary>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(EntropyBytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>SHA-256 của chuỗi session id (UTF-8). Xác định: cùng đầu vào, cùng hash, để tra session mỗi request.</summary>
    public static byte[] Hash(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            throw new ArgumentException("Session id không được rỗng.", nameof(token));
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
