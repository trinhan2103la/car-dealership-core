using System;
using System.Collections.Generic;
using System.Linq;

namespace CarDealership.CoreService.Logging;

/// <summary>
/// Ngày 11 (Req 22, 21): che dữ liệu nhạy cảm trước khi đưa vào log.
/// Dùng cho path/query và tên header, tránh ghi mật khẩu, cookie, Edge key, JWT ra log.
/// Thuần (pure) để unit test dễ.
/// </summary>
public static class LogRedaction
{
    public const string Mask = "***";

    // Tên tham số query / khóa nhạy cảm (so khớp không phân biệt hoa thường).
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "pwd", "pass",
        "token", "access_token", "refresh_token", "id_token", "jwt",
        "authorization", "cookie", "secret", "apikey", "api_key",
        "x-edge-key", "x-admin-session", "code", "client_secret"
    };

    /// <summary>Header có tên nhạy cảm thì KHÔNG được log giá trị.</summary>
    public static bool IsSensitiveHeader(string headerName)
        => !string.IsNullOrEmpty(headerName) && SensitiveKeys.Contains(headerName);

    /// <summary>
    /// Che phần query string nhạy cảm nhưng vẫn giữ path để chẩn đoán.
    /// Ví dụ: /api/app/auth?token=abc&amp;page=2 -> /api/app/auth?token=***&amp;page=2
    /// </summary>
    public static string RedactPathAndQuery(string? pathAndQuery)
    {
        if (string.IsNullOrEmpty(pathAndQuery))
        {
            return string.Empty;
        }

        var qIndex = pathAndQuery.IndexOf('?');
        if (qIndex < 0)
        {
            return pathAndQuery;
        }

        var path = pathAndQuery[..qIndex];
        var query = pathAndQuery[(qIndex + 1)..];

        var redacted = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var eq = pair.IndexOf('=');
                if (eq < 0)
                {
                    return pair;
                }

                var key = pair[..eq];
                return SensitiveKeys.Contains(key) ? $"{key}={Mask}" : pair;
            });

        return $"{path}?{string.Join('&', redacted)}";
    }
}
