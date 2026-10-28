using CarDealership.CoreService.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace CarDealership.CoreService.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(CoreServiceEntityFrameworkCoreModule),
    typeof(CoreServiceApplicationContractsModule)
    )]
public class CoreServiceDbMigratorModule : AbpModule
{
}
