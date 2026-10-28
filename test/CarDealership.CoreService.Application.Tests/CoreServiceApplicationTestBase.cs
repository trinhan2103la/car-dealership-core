using Volo.Abp.Modularity;

namespace CarDealership.CoreService;

public abstract class CoreServiceApplicationTestBase<TStartupModule> : CoreServiceTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
