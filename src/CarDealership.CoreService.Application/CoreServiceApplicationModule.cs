using Volo.Abp.Mapperly;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Microsoft.Extensions.DependencyInjection;

namespace CarDealership.CoreService;

[DependsOn(
    typeof(CoreServiceDomainModule),
    typeof(CoreServiceApplicationContractsModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpSettingManagementApplicationModule)
    )]
public class CoreServiceApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddMapperlyObjectMapper<CoreServiceApplicationModule>();

        // Object_Storage cho ảnh xe: đọc mục "ObjectStorage" (biến môi trường ObjectStorage__*). Thiếu cấu hình chỉ lỗi khi dùng.
        Configure<CarDealership.CoreService.Catalog.Images.ImageStorageOptions>(
            context.Services.GetConfiguration().GetSection(CarDealership.CoreService.Catalog.Images.ImageStorageOptions.Section));
    }
}
