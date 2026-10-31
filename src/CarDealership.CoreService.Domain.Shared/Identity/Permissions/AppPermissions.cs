using System.Collections.Generic;

namespace CarDealership.CoreService.Identity.Permissions;

/// <summary>
/// Req 15.14: đúng 9 Permission của dự án. Tên là khóa tự nhiên (Req 25.1), KHÔNG đổi sau khi đã seed.
/// Đặt ở Domain.Shared để cả seeder (Domain) lẫn PermissionDefinitionProvider (Application.Contracts) dùng chung.
/// </summary>
public static class AppPermissions
{
    public const string CatalogEdit = "Catalog.Edit";
    public const string CatalogPublish = "Catalog.Publish";
    public const string LeadsView = "Leads.View";
    public const string LeadsManage = "Leads.Manage";
    public const string ShowroomsManage = "Showrooms.Manage";
    public const string UsersManage = "Users.Manage";
    public const string SettingsManage = "Settings.Manage";
    public const string AuditLogView = "AuditLog.View";
    public const string AnalyticsView = "Analytics.View";

    public static readonly IReadOnlyList<string> All = new[]
    {
        CatalogEdit, CatalogPublish,
        LeadsView, LeadsManage,
        ShowroomsManage,
        UsersManage,
        SettingsManage,
        AuditLogView,
        AnalyticsView
    };
}
