using Volo.Abp.Modularity;

namespace CarDealership.CoreService;

/* Inherit from this class for your domain layer tests. */
public abstract class CoreServiceDomainTestBase<TStartupModule> : CoreServiceTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{

}
