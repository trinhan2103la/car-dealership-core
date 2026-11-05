using System.Collections.Generic;

namespace CarDealership.CoreService.Identity;

/// <summary>
/// 4 Role cố định của MVP. Tên là khóa tự nhiên, KHÔNG đổi sau khi đã seed.
/// </summary>
public static class AppRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string ContentEditor = "ContentEditor";
    public const string SalesStaff = "SalesStaff";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = new[]
    {
        SuperAdmin, ContentEditor, SalesStaff, Viewer
    };
}
