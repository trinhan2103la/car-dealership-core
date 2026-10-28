using System.Linq;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace CarDealership.CoreService;

/// <summary>
/// Ngày 10 (Req 29.5): ẩn mọi controller do module ABP tự đăng ký
/// (Identity, PermissionManagement, SettingManagement, lõi Abp...) khỏi routing,
/// để các endpoint /api/identity/*, /api/permission-management/*, /api/setting-management/*,
/// /api/abp/* trả 404. Controller của dự án (namespace CarDealership.CoreService.*)
/// được giữ lại và phải viết tường minh.
/// </summary>
public class HideAbpModuleControllersConvention : IApplicationModelConvention
{
    public void Apply(ApplicationModel application)
    {
        // Loại HẲN controller không thuộc dự án khỏi ApplicationModel.
        // (Không dùng Selectors.Clear() vì nó để lại controller route rỗng -> va nhau
        //  gây AmbiguousMatchException khi framework quét endpoint, ví dụ lúc mở Swagger.)
        var toRemove = application.Controllers
            .Where(c => !(c.ControllerType.Namespace ?? string.Empty)
                .StartsWith("CarDealership.CoreService"))
            .ToList();

        foreach (var controller in toRemove)
        {
            application.Controllers.Remove(controller);
        }
    }
}
