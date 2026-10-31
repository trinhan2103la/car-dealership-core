using CarDealership.CoreService.Identity.Permissions;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace CarDealership.CoreService.Permissions;

/// <summary>
/// Req 15.14: khai báo đúng 9 Permission bằng permission system của ABP.
/// Tên hiển thị là chuỗi cố định (tiếng Anh) vì chưa có UI quản lý Permission; thêm khóa localization
/// khi một UI thật sự cần hiển thị.
/// </summary>
public class CoreServicePermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var group = context.AddGroup(CoreServicePermissions.GroupName, Fixed("Car Dealership"));

        foreach (var name in AppPermissions.All)
        {
            group.AddPermission(name, Fixed(name));
        }
    }

    private static ILocalizableString Fixed(string text) => new FixedLocalizableString(text);
}
