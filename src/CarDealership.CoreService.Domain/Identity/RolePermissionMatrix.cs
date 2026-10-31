using System.Collections.Generic;
using CarDealership.CoreService.Identity.Permissions;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// Req 15.4: ma trận Role × Permission được seed. Mọi Permission không liệt kê bị từ chối.
/// SalesStaff chỉ có Leads.View/Leads.Manage "trong Showroom_Scope": phạm vi dữ liệu đó là việc của
/// application service ở P3/P6, không phải của ma trận này.
/// </summary>
public static class RolePermissionMatrix
{
    public static IReadOnlyList<string> PermissionsOf(string role) => role switch
    {
        AppRoles.SuperAdmin => AppPermissions.All,
        AppRoles.ContentEditor => new[]
        {
            AppPermissions.CatalogEdit, AppPermissions.CatalogPublish, AppPermissions.SettingsManage
        },
        AppRoles.SalesStaff => new[]
        {
            AppPermissions.LeadsView, AppPermissions.LeadsManage
        },
        AppRoles.Viewer => new[]
        {
            AppPermissions.AnalyticsView
        },
        _ => throw new KeyNotFoundException($"Role khong thuoc ma tran: {role}")
    };
}
