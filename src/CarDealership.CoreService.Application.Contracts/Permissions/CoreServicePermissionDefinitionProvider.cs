using CarDealership.CoreService.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace CarDealership.CoreService.Permissions;

public class CoreServicePermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(CoreServicePermissions.GroupName);
        //Define your own permissions here. Example:
        //myGroup.AddPermission(CoreServicePermissions.MyPermission1, L("Permission:MyPermission1"));
    }

    private static LocalizableString L(string name)    {
        return LocalizableString.Create<CoreServiceResource>(name);
    }
}
