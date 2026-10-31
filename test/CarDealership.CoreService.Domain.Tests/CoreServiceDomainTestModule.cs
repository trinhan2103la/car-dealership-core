using CarDealership.CoreService.Identity;
using Volo.Abp.Modularity;

namespace CarDealership.CoreService;

[DependsOn(
    typeof(CoreServiceDomainModule),
    typeof(CoreServiceTestBaseModule)
)]
public class CoreServiceDomainTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Ngoài test, SuperAdmin lấy từ biến môi trường SEED_SUPERADMIN_*. Test cấp tài khoản cố định
        // để mọi test khởi động với đúng một SuperAdmin đã biết (không phụ thuộc môi trường máy).
        Configure<SuperAdminSeedOptions>(options =>
        {
            options.Email = CoreServiceTestConsts.SuperAdminEmail;
            options.Password = CoreServiceTestConsts.SuperAdminPassword;
        });
    }
}
