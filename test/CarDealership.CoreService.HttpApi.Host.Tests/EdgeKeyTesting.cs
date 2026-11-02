using System.Collections.Generic;
using System.Net.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CarDealership.CoreService;

/// <summary>
/// Ngày 36: Core_API từ chối mọi request thiếu X-Edge-Key (trừ GET /health/live), nên mọi host test phải cấp khóa.
/// Khóa này CHỈ để test, không phải bí mật. Dài ≥ 32 ký tự để qua kiểm tra khởi động.
/// </summary>
public static class EdgeKeyTesting
{
    public const string Key = "test-edge-key-0123456789abcdef0123456789abcdef";
    public const string Header = "X-Edge-Key";

    public static void UseKey(IWebHostBuilder builder, string? key)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { [CarDealership.CoreService.Middleware.EdgeKeyOptions.VariableName] = key }));
    }

    public static void AddDefaultHeader(HttpClient client, string key = Key)
    {
        client.DefaultRequestHeaders.TryAddWithoutValidation(Header, key);
    }
}