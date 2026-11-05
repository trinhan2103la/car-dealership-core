using System;
using System.Security.Cryptography;
using System.Text;

namespace CarDealership.CoreService.Identity.Sessions;

/// <summary>
/// Session id mờ 256 bit. Giá trị gốc CHỈ trả cho client một lần (cookie qua BFF);
/// Core_API chỉ lưu <see cref="Hash"/> (SHA-256). Không bao giờ dùng Guid (chỉ 122 bit ngẫu nhiên).
/// </summary>
public static class SessionToken
{
    public const int EntropyBytes = 32;       // 256 bit
    public const int HashBytes = 32;          // SHA-256
    public const int EncodedLength = 43;      // 32 byte -> base64url không đệm

    /// <summary>Sinh session id mới bằng RandomNumberGenerator (CSPRNG), mã hóa base64url không đệm (43 ký tự).</summary>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(EntropyBytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// Đúng dạng do <see cref="Generate"/> sinh (43 ký tự base64url). Kiểm TRƯỚC khi băm / chạm DB để input rác
    /// (header tùy ý của client) bị loại sớm và rẻ.
    /// </summary>
    public static bool IsWellFormed(string? token)
    {
        if (token is null || token.Length != EncodedLength)
        {
            return false;
        }

        foreach (var c in token)
        {
            var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
            if (!ok)
            {
                return false;
            }
        }

        return true;
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
