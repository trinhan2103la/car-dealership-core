using CarDealership.CoreService.EntityFrameworkCore;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace CarDealership.CoreService.DbMigrator;

[DependsOn(
    typeof(AbpAutofacModule),
    typeof(CoreServiceEntityFrameworkCoreModule),
    typeof(CoreServiceApplicationContractsModule),
    // S3ImageStorage (IImageStorage) + ImageStorageOptions cho seed catalog demo.
    typeof(CoreServiceApplicationModule)
    )]
public class CoreServiceDbMigratorModule : AbpModule
{
}
