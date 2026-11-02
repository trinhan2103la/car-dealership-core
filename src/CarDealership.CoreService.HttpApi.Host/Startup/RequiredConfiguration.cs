using System;
using System.Collections.Generic;
using System.Linq;
using CarDealership.CoreService.Auth;
using CarDealership.CoreService.Middleware;
using Microsoft.Extensions.Configuration;

namespace CarDealership.CoreService.Startup;

/// <summary>
/// Req 16.12: khởi động thiếu biến môi trường bắt buộc thì thoát process mã khác 0 TRƯỚC khi nhận request và log TÊN
/// mọi biến bị thiếu/không hợp lệ, KHÔNG BAO GIỜ log giá trị. Hàm thuần để unit test; chỗ gọi nằm ở
/// <see cref="CoreServiceHttpApiHostModule"/> (ném exception → Program.cs log Fatal và trả mã 1).
/// Thêm biến bắt buộc mới (khóa công khai ES256, ...) bằng cách thêm một dòng vào <see cref="Find"/>.
/// </summary>
public static class RequiredConfiguration
{
    /// <summary>Danh sách vấn đề dạng "TÊN_BIẾN: lý do" (không chứa giá trị). Rỗng nghĩa là hợp lệ.</summary>
    public static IReadOnlyList<string> Find(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var problems = new List<string>();

        var edgeKey = configuration[EdgeKeyOptions.VariableName];
        if (string.IsNullOrWhiteSpace(edgeKey))
        {
            problems.Add($"{EdgeKeyOptions.VariableName}: thiếu hoặc rỗng");
        }
        else if (edgeKey.Length < EdgeKeyOptions.MinLength)
        {
            problems.Add($"{EdgeKeyOptions.VariableName}: ngắn hơn {EdgeKeyOptions.MinLength} ký tự");
        }

        // Ngày 37: khóa công khai ES256 để kiểm JWT của BFF. Chỉ nêu lý do, KHÔNG bao giờ in nội dung khóa.
        using (var key = ServiceCredentialKey.TryParse(configuration[ServiceCredential.PublicKeyVariable], out var keyProblem))
        {
            if (key is null)
            {
                problems.Add($"{ServiceCredential.PublicKeyVariable}: {keyProblem}");
            }
        }

        return problems;
    }

    /// <exception cref="InvalidOperationException">Thông báo chỉ gồm tên biến và lý do.</exception>
    public static void EnsureValid(IConfiguration configuration)
    {
        var problems = Find(configuration);
        if (problems.Any())
        {
            throw new InvalidOperationException(
                "Khởi động dừng: thiếu hoặc sai biến môi trường bắt buộc: " + string.Join("; ", problems));
        }
    }
}
