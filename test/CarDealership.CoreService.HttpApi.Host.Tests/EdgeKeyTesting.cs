using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace CarDealership.CoreService;

/// <summary>
/// Cấp thông tin xác thực cho host test. Core_API từ chối request thiếu X-Edge-Key (Ngày 36, trừ GET /health/live) và
/// thiếu service JWT hợp lệ (Ngày 37), nên client mặc định của factory tự kèm cả hai; test muốn thử "thiếu" thì
/// gỡ header khỏi client. Các giá trị này CHỈ để test, không phải bí mật.
/// </summary>
public static class EdgeKeyTesting
{
    public const string Key = "test-edge-key-0123456789abcdef0123456789abcdef";
    public const string Header = "X-Edge-Key";

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
            }));
    }

    public static void AddDefaultHeader(HttpClient client, string key = Key)
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation(Header, key);
        // Token sống 60 giây, tạo lúc dựng client: đủ cho một test; đừng giữ client qua nhiều phút.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestServiceJwt.Create());
    }
}