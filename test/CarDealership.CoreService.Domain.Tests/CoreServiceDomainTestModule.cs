using Volo.Abp.Modularity;

namespace CarDealership.CoreService;

[DependsOn(
    typeof(CoreServiceDomainModule),
    typeof(CoreServiceTestBaseModule)
)]
public class CoreServiceDomainTestModule : AbpModule
{

}
