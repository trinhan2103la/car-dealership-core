using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CarDealership.CoreService;

/// <summary>
/// Ngày 37: đóng vai BFF trong test, ký service JWT ES256 bằng một cặp khóa tạo MỚI mỗi lần chạy test (không có khóa nào
/// nằm trong repo). Core_API trong test được cấp đúng khóa công khai của cặp này qua <see cref="EdgeKeyTesting"/>.
/// </summary>
public static class TestServiceJwt
{
    public static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static string PublicKeyPem => Key.ExportSubjectPublicKeyInfoPem();

    public static SigningCredentials Es256(ECDsa? key = null) =>
        new(new ECDsaSecurityKey(key ?? Key), SecurityAlgorithms.EcdsaSha256) { CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } };

    /// <summary>Payload hợp lệ theo ADR-004; test sửa từng phần để dựng token sai.</summary>
    public static Dictionary<string, object> ValidPayload(string scope = "bff", DateTime? now = null)
    {
        var issued = new DateTimeOffset(now ?? DateTime.UtcNow).ToUnixTimeSeconds();
        return new Dictionary<string, object>
        {
            ["iss"] = "bff",
            ["aud"] = "core-api",
            ["iat"] = issued,
            ["exp"] = issued + 60,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["cid"] = Guid.NewGuid().ToString("N"),
            ["scope"] = scope,
        };
    }

    /// <summary>Token hợp lệ, hết hạn sau 60 giây; mặc định phát hành lùi 2 giây để khỏi vướng lệch đồng hồ.</summary>
    public static string Create(string scope = "bff") =>
        Create(ValidPayload(scope, DateTime.UtcNow.AddSeconds(-2)));

    public static string Create(Dictionary<string, object> payload, SigningCredentials? credentials = null) =>
        new JsonWebTokenHandler().CreateToken(JsonSerializer.Serialize(payload), credentials ?? Es256());

    /// <summary>Token KHÔNG chữ ký (alg = none).</summary>
    public static string CreateUnsigned(Dictionary<string, object> payload)
    {
        static string B64(string json) => Base64UrlEncoder.Encode(json);
        return B64("{\"alg\":\"none\",\"typ\":\"JWT\"}") + "." + B64(JsonSerializer.Serialize(payload)) + ".";
    }
}