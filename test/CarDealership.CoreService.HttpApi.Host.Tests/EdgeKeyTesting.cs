using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace CarDealership.CoreService;

/// <summary>
/// Cấp thông tin xác thực cho host test. Core_API từ chối request thiếu X-Edge-Key (trừ GET /health/live) và
/// thiếu service JWT hợp lệ, nên client mặc định của factory tự kèm cả hai; test muốn thử "thiếu" thì
/// gỡ header khỏi client. Các giá trị này CHỈ để test (sinh ngẫu nhiên mỗi lần chạy), không phải bí mật.
/// </summary>
public static class EdgeKeyTesting
{
    // Sinh NGẪU NHIÊN mỗi lần chạy test (không có chuỗi giống bí mật nào nằm trong mã nguồn nên gitleaks không báo, và cũng
    // không có "khóa test" cố định nào để ai đó dùng nhầm). 24 byte -> 48 ký tự hex, ≥ 32 ký tự để qua kiểm tra khởi động.
    public static readonly string Key = "edge-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    public const string Header = "X-Edge-Key";

    /// <summary>Passphrase mã hóa dùng-một-lần cho host test (biến bắt buộc khi khởi động, như Edge key).</summary>
    public static readonly string Passphrase = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public static void UseKey(IWebHostBuilder builder, string? key) =>
        UseConfiguration(builder, key, TestServiceJwt.PublicKeyPem);

    /// <summary>Cấp Edge key và khóa công khai ES256 tùy ý (để dựng ca cấu hình sai). Khóa riêng không rời khỏi tiến trình test.</summary>
    public static void UseConfiguration(IWebHostBuilder builder, string? edgeKey, string? servicePublicKey)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CarDealership.CoreService.Middleware.EdgeKeyOptions.VariableName] = edgeKey,
                [CarDealership.CoreService.Auth.ServiceCredential.PublicKeyVariable] = servicePublicKey,
                [CarDealership.CoreService.Startup.StringEncryptionOptions.VariableName] = Passphrase,
            }));
    }

    public static void AddDefaultHeader(HttpClient client, string? key = null)
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation(Header, key ?? Key);
        // Token sống 60 giây, tạo lúc dựng client: đủ cho một test; đừng giữ client qua nhiều phút.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestServiceJwt.Create());
    }
}