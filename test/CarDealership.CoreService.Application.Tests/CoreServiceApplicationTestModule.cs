using Volo.Abp.Modularity;

namespace CarDealership.CoreService;

[DependsOn(
    typeof(CoreServiceApplicationModule),
    typeof(CoreServiceDomainTestModule)
)]
public class CoreServiceApplicationTestModule : AbpModule
{

}
